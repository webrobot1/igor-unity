using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Mmogick
{
	/// <summary>
	/// Держит полупрозрачное «окно» вокруг СВОЕГО игрока в тех слоях карты, которые его перекрывают
	/// (кроны деревьев, крыши, верхние этажи): зашедший под них игрок не теряется из вида. Окно одно —
	/// только у своего игрока (решение автора 2026-10-01): перекрывающий слой непрозрачен, пока свой игрок
	/// не шагнул в клетку под ним, — ни подход к нему, ни чужие игроки и существа, ни метки переходов под
	/// аркой его не открывают.
	///
	/// Слой перекрывает игрока ⟺ его порядок отрисовки больше порядка игрока (земля его этажа, см.
	/// UpdateController). Порог берётся у SortingGroup игрока каждый кадр, а не запоминается на карту:
	/// уровень z игрока меняется (переход, смена этажа) — набор гасимых слоёв следует за этажом сам.
	///
	/// Само гашение — в шейдере Mmogick/TilemapXray: сюда компонент шлёт лишь центр окна
	/// (мировые xy, порядок игрока, радиус) глобальным вектором. Гаснет только реально нарисованное:
	/// на открытом месте у перекрывающих слоёв тайлов нет, поэтому проплешины вокруг игрока не видно.
	///
	/// Живёт на контейнере карт (Map), навешивается кодом из MapController — статического места в
	/// сцене не требует.
	/// </summary>
	public class TilemapXray : MonoBehaviour
	{
		private const string MaterialResource = "Materials/TilemapXray";

		/// <summary>Радиус окна в клетках — чуть шире самой сущности, чтобы её силуэт читался целиком.</summary>
		private const float Radius = 1.5f;

		/// <summary>
		/// Подъём центра окна над позицией сущности: сущность привязана НОГАМИ (см. MapController.TILE_OFFSET),
		/// а прикрыто кроной оказывается тело — окно центрируем по нему, иначе верх спрайта остаётся за листвой.
		/// </summary>
		private const float CenterOffsetY = 0.5f;

		private static Material _material;

		private static readonly int CenterId     = Shader.PropertyToID("_XrayCenter");
		private static readonly int LayerOrderId = Shader.PropertyToID("_LayerOrder");
		private static readonly int ColorId      = Shader.PropertyToID("_Color");

		/// <summary>Вешает компонент на контейнер карт; повторный вызов ничего не меняет.</summary>
		public static void Attach(GameObject host)
		{
			if (host.GetComponent<TilemapXray>() == null)
				host.AddComponent<TilemapXray>();
		}

		/// <summary>
		/// Переводит слои карты, способные перекрыть игрока (порядок больше земли первого этажа), на
		/// xray-материал и сообщает каждому его порядок отрисовки. Вызывается один раз на карту, после
		/// MapDecodeModel.generate. Отладочные слои пропускаются — они служебные и лежат поверх всего by-design.
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

				renderer.sharedMaterial = Instance(material, renderer.sortingOrder, tint);
			}
		}

		/// <summary>
		/// Материал слоя по его порядку отрисовки и оттенку. Экземпляр нужен свой: через
		/// MaterialPropertyBlock значения не передать — блок ставится на ВЕСЬ рендерер и перебивает
		/// текстуру, которую тайлы подставляют каждый свою (у тайла собственная картинка, общего
		/// атласа нет — TileCacheService), отчего слой рисуется чужими тайлами.
		///
		/// Экземпляры ОБЩИЕ на пару значений, а не по одному на слой: карты выкладываются и сносятся на
		/// каждом переходе открытого мира, а созданный кодом материал уничтожения рендерера не переживает
		/// как ассет — он остаётся жить сиротой, и за сессию их копились бы сотни. Пар же немного:
		/// перекрывающих слоёв у карты единицы, а порядок и оттенок у одноимённых слоёв соседних карт
		/// совпадают. Набор живёт, пока идёт игра; уничтоженный экземпляр отсекает Unity-проверка.
		/// </summary>
		private static Material Instance(Material source, int order, Color tint)
		{
			var key = (order, (Color32)tint);
			if (_instances.TryGetValue(key, out Material known) && known != null)
				return known;

			Material instance = new Material(source);
			instance.SetFloat(LayerOrderId, order);
			instance.SetColor(ColorId, tint);

			_instances[key] = instance;
			return instance;
		}

		private static readonly Dictionary<(int, Color32), Material> _instances =
			new Dictionary<(int, Color32), Material>();

		private static Material GetMaterial()
		{
			if (_material != null)
				return _material;

			_material = Resources.Load<Material>(MaterialResource);
			if (_material == null)
			{
				ConnectController.Error("TilemapXray: не найден Resources/" + MaterialResource + ".mat (шейдер Mmogick/TilemapXray) — "
					+ "сущности под кронами и крышами останутся невидимыми");
				return null;
			}

			return _material;
		}

		/// <summary>
		/// Отдаёт шейдеру окно своего игрока; игрока на карте нет — радиус 0, окна нет. LateUpdate — после того
		/// как игрок доехал в свою позицию этого кадра, иначе окно отстаёт от тела на кадр.
		/// </summary>
		private void LateUpdate()
		{
			if (_material == null)
				return;

			Vector4 center = Vector4.zero;

			// Порядок отрисовки берём у группы сортировки игрока: её ссылку модель держит готовой
			// (EntityModel.EnsureRenderRefs заполняет её на каждый пакет).
			EntityModel player = ConnectController.OwnPlayer;
			if (player != null && player.gameObject.activeInHierarchy)
			{
				player.EnsureRenderRefs();
				SortingGroup group = player.sortingGroup;
				Vector3 position = player.transform.position;

				// Окно — с шага под перекрывающий слой, не на подходе (решение автора 2026-10-01): пока клетка под
				// ногами не накрыта, круг у края крыши бледнил бы её до того, как игрок под неё зашёл.
				Vector2Int cell = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
				if (group != null && MapController.IsCoveredCell(cell, group.sortingOrder))
					center = new Vector4(position.x, position.y + CenterOffsetY, group.sortingOrder, Radius);
			}

			Shader.SetGlobalVector(CenterId, center);
		}
	}
}
