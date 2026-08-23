using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Mmogick
{
	/// <summary>
	/// Держит полупрозрачное «окно» вокруг сущностей в тех слоях карты, которые их перекрывают
	/// (кроны деревьев, крыши): зашедший под них игрок, моб или лежащий предмет не теряется из вида.
	/// Метки переходов (WarpMarker) окно получают наравне с сущностями: метка лежит на земле, и дверной
	/// проём под аркой перекрывает её так же, как крона перекрывает игрока.
	///
	/// Слой перекрывает сущность ⟺ его порядок отрисовки больше порядка сущности
	/// (spawn_sort + серверный sort, см. UpdateController). Порог берётся у КАЖДОЙ сущности из её
	/// SortingGroup, а не запоминается на карту: сервер меняет sort при смене этажа (событие
	/// object/portal шлёт sort вместе с z) — набор гасимых слоёв следует за этажом сам.
	///
	/// Само гашение — в шейдере Mmogick/TilemapXray: сюда компонент шлёт лишь центры окон
	/// (мировые xy, порядок сущности, радиус) глобальным массивом. Гаснет только реально нарисованное:
	/// на открытом месте у перекрывающих слоёв тайлов нет, поэтому проплешины вокруг игрока не видно.
	///
	/// Живёт на контейнере карт (Map), навешивается кодом из MapController — статического места в
	/// сцене не требует.
	/// </summary>
	public class TilemapXray : MonoBehaviour
	{
		/// <summary>Максимум одновременных окон. Держать синхронно с XRAY_MAX шейдера.</summary>
		public const int MaxCenters = 16;

		private const string MaterialResource = "Materials/TilemapXray";

		/// <summary>Радиус окна в клетках — чуть шире самой сущности, чтобы её силуэт читался целиком.</summary>
		private const float Radius = 1.5f;

		/// <summary>
		/// Подъём центра окна над позицией сущности: сущность привязана НОГАМИ (см. MapController.TILE_OFFSET),
		/// а прикрыто кроной оказывается тело — окно центрируем по нему, иначе верх спрайта остаётся за листвой.
		/// </summary>
		private const float CenterOffsetY = 0.5f;

		/// <summary>Запас за краем экрана (в долях вьюпорта): окна вне видимости не тратим.</summary>
		private const float ViewMargin = 0.2f;

		private static Material _material;
		private static bool _materialMissing;

		/// <summary>
		/// Ширина мягкого края окна в клетках — берётся у материала, а не задаётся здесь вторым числом:
		/// шейдер растушёвывает край ВНУТРЬ радиуса, и радиус окна метки считается с этим запасом.
		/// </summary>
		private static float _softness;

		private static readonly Vector4[] _centers = new Vector4[MaxCenters];
		private static readonly List<Vector4> _found = new List<Vector4>();
		private static readonly List<Vector4> _foundWarps = new List<Vector4>();

		/// <summary>Точка, от которой меряется удаление при отсечении лишних окон (центр кадра).</summary>
		private static Vector2 _sortCenter;

		/// <summary>
		/// Готовый компаратор — статикой, а не лямбдой по месту: сортировка идёт в кадре, а замыкание
		/// над центром кадра давало бы мусор каждый кадр (см. «Замер производительности клиента»).
		/// </summary>
		private static readonly Comparison<Vector4> ByDistanceToCenter = (a, b) =>
			new Vector2(a.x - _sortCenter.x, a.y - _sortCenter.y).sqrMagnitude
				.CompareTo(new Vector2(b.x - _sortCenter.x, b.y - _sortCenter.y).sqrMagnitude);

		private static readonly int CentersId    = Shader.PropertyToID("_XrayCenters");
		private static readonly int CountId      = Shader.PropertyToID("_XrayCount");
		private static readonly int LayerOrderId = Shader.PropertyToID("_LayerOrder");
		private static readonly int ColorId      = Shader.PropertyToID("_Color");
		private static readonly int SoftnessId   = Shader.PropertyToID("_XraySoftness");

		private Transform _world;
		private Camera _cam;

		/// <summary>
		/// Вешает компонент на контейнер карт и запоминает контейнер сущностей (World), по которому
		/// каждый кадр собираются центры окон. Повторный вызов только обновляет ссылку.
		/// </summary>
		public static void Attach(GameObject host, GameObject worldObject)
		{
			TilemapXray xray = host.GetComponent<TilemapXray>();
			if (xray == null)
				xray = host.AddComponent<TilemapXray>();

			xray._world = worldObject.transform;
		}

		/// <summary>
		/// Переводит слои карты, способные перекрыть сущность (порядок больше слоя-земли), на xray-материал
		/// и сообщает каждому его порядок отрисовки. Вызывается один раз на карту, после MapDecodeModel.generate.
		/// Отладочные слои пропускаются — они служебные и лежат поверх всего by-design.
		/// </summary>
		public static void RegisterMap(Transform grid, int spawnSort)
		{
			Material material = GetMaterial();
			if (material == null)
				return;

			foreach (Transform child in grid)
			{
				if (child.name == DebugLayers.GRID || child.name == DebugLayers.COLLISION || child.name == DebugLayers.OBJECTS)
					continue;

				TilemapRenderer renderer = child.GetComponent<TilemapRenderer>();
				if (renderer == null || renderer.sortingOrder <= spawnSort)
					continue;

				// Слой мог получить собственную прозрачность (layer.opacity кладётся в _Color материала
				// в MapDecodeModel) — подмена материала её бы стёрла, переносим в новый материал.
				Color tint = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(ColorId)
					? renderer.sharedMaterial.color
					: Color.white;

				// Свой экземпляр материала на слой: порядок отрисовки у каждого свой. Через
				// MaterialPropertyBlock его передать нельзя — блок ставится на ВЕСЬ рендерер и перебивает
				// текстуру, которую тайлы подставляют каждый свою (у тайла собственная картинка, общего
				// атласа нет — TileCacheService), отчего слой рисуется чужими тайлами.
				Material instance = new Material(material);
				instance.SetFloat(LayerOrderId, renderer.sortingOrder);
				instance.SetColor(ColorId, tint);

				renderer.sharedMaterial = instance;
			}
		}

		private static Material GetMaterial()
		{
			if (_material != null || _materialMissing)
				return _material;

			_material = Resources.Load<Material>(MaterialResource);
			if (_material == null)
			{
				_materialMissing = true;
				Debug.LogError("TilemapXray: не найден Resources/" + MaterialResource + ".mat (шейдер Mmogick/TilemapXray) — "
					+ "сущности под кронами и крышами останутся невидимыми");
			}
			else
				_softness = _material.GetFloat(SoftnessId);

			return _material;
		}

		/// <summary>
		/// Собирает центры окон по видимым сущностям и меткам переходов и отдаёт их шейдеру. LateUpdate —
		/// после того как сущности и камера доехали в свои позиции этого кадра, иначе окно отстаёт от тела
		/// на кадр.
		/// </summary>
		private void LateUpdate()
		{
			if (_material == null || _world == null)
				return;

			if (_cam == null)
				_cam = Camera.main;
			if (_cam == null)
				return;

			_found.Clear();
			_foundWarps.Clear();

			foreach (Transform map in _world)
			{
				foreach (Transform child in map)
				{
					if (!child.gameObject.activeInHierarchy)
						continue;

					// SortingGroup на корне сущности — источник её порядка отрисовки (UpdateController).
					SortingGroup group = child.GetComponent<SortingGroup>();
					if (group == null || child.GetComponent<EntityModel>() == null)
						continue;

					Vector3 position = child.position;
					if (!IsOnScreen(position))
						continue;

					_found.Add(new Vector4(position.x, position.y + CenterOffsetY, group.sortingOrder, Radius));
				}
			}

			// Метки переходов лежат в самой карте, а не в World, поэтому идут отдельным проходом — тем же,
			// каким их обходит радар (MinimapController.DrawWarps). Компонент живёт на контейнере карт,
			// значит transform и есть контейнер.
			foreach (Transform map in transform)
			{
				Transform warps = map.Find(WarpMarker.LAYER);
				if (warps == null)
					continue;   // игра переходами по разметке не пользуется либо карта ещё строится

				foreach (Transform warp in warps)
				{
					SpriteRenderer marker = warp.GetComponent<SpriteRenderer>();
					if (marker == null || !warp.gameObject.activeInHierarchy)
						continue;

					Bounds area = marker.bounds;
					if (!IsOnScreen(area.center))
						continue;

					// Переход — площадь на полу, а не силуэт на ногах: центр берём у самой площади, радиус —
					// по описанной вокруг неё окружности, чтобы окно накрыло метку целиком и на широком
					// проходе. Плюс растушёвка: её шейдер съедает внутрь радиуса.
					_foundWarps.Add(new Vector4(area.center.x, area.center.y, marker.sortingOrder,
						area.extents.magnitude + _softness));
				}
			}

			_sortCenter = _cam.transform.position;

			// Слоты сущностям достаются первыми: под крышей теряется управляемое тело, а метка перехода —
			// неподвижная подсказка на полу, её потеря стоит дешевле. Метки добирают остаток.
			int count = Fill(_found, 0);
			count = Fill(_foundWarps, count);

			for (int i = count; i < MaxCenters; i++)
				_centers[i] = Vector4.zero;

			// Массив шлём целиком всегда: длину глобального массива Unity фиксирует по первому вызову.
			Shader.SetGlobalVectorArray(CentersId, _centers);
			Shader.SetGlobalInt(CountId, count);
		}

		/// <summary>
		/// Переносит найденные окна в массив шейдера начиная с позиции from и возвращает новую границу
		/// заполненного. Свободных слотов меньше найденного — оставляем ближние к центру кадра: свой игрок
		/// там всегда (камера следует за ним), а дальнее теряется на краю кадра и без окна.
		/// </summary>
		private static int Fill(List<Vector4> found, int from)
		{
			int free = MaxCenters - from;
			if (free <= 0)
				return from;   // слотов не осталось — сортировать отсекаемое незачем

			if (found.Count > free)
				found.Sort(ByDistanceToCenter);

			int take = Mathf.Min(found.Count, free);
			for (int i = 0; i < take; i++)
				_centers[from + i] = found[i];

			return from + take;
		}

		/// <summary>Видна ли точка в кадре с запасом ViewMargin: окно вне видимости слота не занимает.</summary>
		private bool IsOnScreen(Vector3 position)
		{
			Vector3 viewport = _cam.WorldToViewportPoint(position);

			return viewport.z >= 0
				&& viewport.x >= -ViewMargin && viewport.x <= 1 + ViewMargin
				&& viewport.y >= -ViewMargin && viewport.y <= 1 + ViewMargin;
		}
	}
}
