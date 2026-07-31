using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Mmogick
{
	/// <summary>
	/// Держит полупрозрачное «окно» вокруг сущностей в тех слоях карты, которые их перекрывают
	/// (кроны деревьев, крыши): зашедший под них игрок, моб или лежащий предмет не теряется из вида.
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

		private static readonly Vector4[] _centers = new Vector4[MaxCenters];
		private static readonly List<Vector4> _found = new List<Vector4>();

		private static readonly int CentersId    = Shader.PropertyToID("_XrayCenters");
		private static readonly int CountId      = Shader.PropertyToID("_XrayCount");
		private static readonly int LayerOrderId = Shader.PropertyToID("_LayerOrder");
		private static readonly int ColorId      = Shader.PropertyToID("_Color");

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

			return _material;
		}

		/// <summary>
		/// Собирает центры окон по видимым сущностям и отдаёт их шейдеру. LateUpdate — после того как
		/// сущности и камера доехали в свои позиции этого кадра, иначе окно отстаёт от тела на кадр.
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
					Vector3 viewport = _cam.WorldToViewportPoint(position);
					if (viewport.z < 0
						|| viewport.x < -ViewMargin || viewport.x > 1 + ViewMargin
						|| viewport.y < -ViewMargin || viewport.y > 1 + ViewMargin)
						continue;

					_found.Add(new Vector4(position.x, position.y + CenterOffsetY, group.sortingOrder, Radius));
				}
			}

			// Окон больше лимита — оставляем ближние к центру экрана: свой игрок там всегда (камера
			// следует за ним), а дальние сущности теряются на краю кадра и без окна.
			if (_found.Count > MaxCenters)
			{
				Vector3 center = _cam.transform.position;
				_found.Sort((a, b) =>
					new Vector2(a.x - center.x, a.y - center.y).sqrMagnitude
						.CompareTo(new Vector2(b.x - center.x, b.y - center.y).sqrMagnitude));
			}

			int count = Mathf.Min(_found.Count, MaxCenters);
			for (int i = 0; i < MaxCenters; i++)
				_centers[i] = i < count ? _found[i] : Vector4.zero;

			// Массив шлём целиком всегда: длину глобального массива Unity фиксирует по первому вызову.
			Shader.SetGlobalVectorArray(CentersId, _centers);
			Shader.SetGlobalInt(CountId, count);
		}
	}
}
