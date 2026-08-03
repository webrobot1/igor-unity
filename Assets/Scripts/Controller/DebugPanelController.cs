using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Debug-панель отладочных слоёв карты. Три галочки управляют видимостью per-map слоёв:
    /// «Коллизии» → DebugCollision, «Сетка» → DebugGrid, «Полигоны» → DebugObjects (объекты-разметка).
    ///
    /// Источник истины видимости — состояние галочек, которое контроллер кладёт в DebugLayers.Show*
    /// (firstpass-holder), НЕ жёсткий ConnectController.isDebug. Слои создаются per-map в
    /// MapDecodeModel.generate и читают DebugLayers.Show* прямо при создании (карты грузятся асинхронно,
    /// ПОЗЖЕ входа — вне жизненного цикла Awake этого контроллера, потому состояние static в firstpass).
    /// Переключение галочки применяется ко ВСЕМ уже загруженным картам; будущие карты берут состояние
    /// из DebugLayers в момент generate.
    /// </summary>
    abstract public class DebugPanelController : CombatTextController
    {
        [Header("Debug-панель отладочных слоёв карты")]

        [SerializeField]
        private Toggle collisionToggle;   // «Коллизии» → слой DebugCollision

        [SerializeField]
        private Toggle gridToggle;        // «Сетка» → слой DebugGrid

        [SerializeField]
        private Toggle polygonToggle;     // «Полигоны» → слой DebugObjects (объекты-разметка)

        protected override void Awake()
        {
            base.Awake();

            if (collisionToggle == null)
            {
                Error("Debug-панель: не назначен Toggle «Коллизии»");
                return;
            }

            if (gridToggle == null)
            {
                Error("Debug-панель: не назначен Toggle «Сетка»");
                return;
            }

            if (polygonToggle == null)
            {
                Error("Debug-панель: не назначен Toggle «Полигоны»");
                return;
            }

            collisionToggle.onValueChanged.AddListener(v => { DebugLayers.ShowCollision = v; ApplyToLoadedMaps(DebugLayers.COLLISION, v); });
            gridToggle.onValueChanged.AddListener(v => { DebugLayers.ShowGrid = v; ApplyToLoadedMaps(DebugLayers.GRID, v); });
            polygonToggle.onValueChanged.AddListener(v => { DebugLayers.ShowObjects = v; ApplyToLoadedMaps(DebugLayers.OBJECTS, v); });
        }

        /// <summary>
        /// Тестовый режим: блок отладочных слоёв показывается только в нём. Выключили — гасим и сами слои,
        /// иначе они остались бы нарисованы поверх карты без возможности их убрать.
        /// </summary>
        protected override void SetTestMode(bool enabled)
        {
            base.SetTestMode(enabled);

            if (collisionToggle == null)
                return;

            // Блок — родитель галочек: отдельной ссылки на него не заводим, чтобы не плодить настройку сцены,
            // которую легко забыть назначить.
            collisionToggle.transform.parent.gameObject.SetActive(enabled);

            if (!enabled)
            {
                collisionToggle.isOn = false;
                gridToggle.isOn = false;
                polygonToggle.isOn = false;
            }
        }

        // Слои по умолчанию выключены: их показывает только тестовый режим игрока (настройка приходит с
        // сервера уже после входа). Строятся они тоже лишь при включении — см. ApplyToLoadedMaps.
        protected virtual void Start()
        {
            DebugLayers.ShowCollision = false;
            DebugLayers.ShowGrid = false;
            DebugLayers.ShowObjects = false;

            // До прихода настроек блок скрыт: у обычного игрока он не появится вовсе.
            collisionToggle.transform.parent.gameObject.SetActive(false);

            // Синхронизируем визуал галочек без вызова обработчика; к уже загруженным картам (если какая-то
            // успела прийти до Start) применяем состояние явно ниже.
            collisionToggle.SetIsOnWithoutNotify(DebugLayers.ShowCollision);
            gridToggle.SetIsOnWithoutNotify(DebugLayers.ShowGrid);
            polygonToggle.SetIsOnWithoutNotify(DebugLayers.ShowObjects);

            ApplyToLoadedMaps(DebugLayers.COLLISION, DebugLayers.ShowCollision);
            ApplyToLoadedMaps(DebugLayers.GRID, DebugLayers.ShowGrid);
            ApplyToLoadedMaps(DebugLayers.OBJECTS, DebugLayers.ShowObjects);
        }

        // Применить видимость слоя ко всем уже загруженным картам (слой ищется по имени в иерархии каждой карты).
        // Включение — момент, когда слой и строится: до первого включения его на карте нет вовсе (три отладочных
        // слоя вместе накрывают карту больше тридцати тысяч клеток, и строить их всем подряд незачем).
        private void ApplyToLoadedMaps(string layerName, bool visible)
        {
            if (mapObject == null)
                return;

            foreach (Transform grid in mapObject.transform)
            {
                if (visible)
                    MapDecodeModel.EnsureDebugLayer(grid, layerName);

                Transform layer = grid.Find(layerName);
                if (layer != null)
                    layer.gameObject.SetActive(visible);
            }
        }
    }
}
