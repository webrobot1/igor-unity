using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Клиентская мини-карта (радар вокруг игрока). Сервер её НЕ шлёт — всё берётся из уже построенной
    /// клиентом тайл-карты (mapObject) и живого мира (worldObject).
    ///
    /// Фон — реальная графика тайлов: отдельная ортографическая камера <see cref="minimapCamera"/> висит
    /// над картой, следует за игроком (позиция XY = позиция игрока) и рендерит в маленький RenderTexture,
    /// показанный <see cref="RawImage"/> в углу. Culling камеры — только слой тайлов «Minimap» (см. ниже),
    /// чтобы не тянуть сущности, мировой UI (LifeBar/боевой текст) и эффекты.
    ///
    /// Маркеры сущностей — UI-точки поверх (<see cref="entityMarkerPrefab"/>), позиция считается как
    /// разница МИРОВЫХ позиций (сущность − игрок) × масштаб. И камера, и маркеры работают в мировых
    /// координатах: TILE_OFFSET уже «запечён» в позициях тайлов (MapController.SortMap сдвигает grid),
    /// камера видит мир как есть, а сущности (zone) стоят на чистой позиции — поэтому точка над сущностью
    /// ложится ровно туда же, где сущность видна в основном окне относительно игрока. Отдельно offset
    /// применять НЕ нужно: он в одной системе координат с камерой.
    ///
    /// Выравнивание держится за счёт того, что слой тайлов «Minimap» (Tilemap.prefab.m_Layer) виден и
    /// основной камерой (её cullingMask = Everything), и minimap-камерой — единственный источник фона.
    /// </summary>
    abstract public class MinimapController : PlayerController
    {
        [Header("Мини-карта (радар)")]

        /// <summary>UI-панель мини-карты целиком (RawImage + рамка). Toggle-клавишей скрывается/показывается.</summary>
        [SerializeField]
        private GameObject minimapRoot;

        /// <summary>Ортокамера-радар: targetTexture = RenderTexture панели, cullingMask = только слой «Minimap».</summary>
        [SerializeField]
        private Camera minimapCamera;

        /// <summary>Основная игровая камера — источник охвата радара (его orthographicSize может меняться в рантайме).</summary>
        [SerializeField]
        private Camera mainCamera;

        /// <summary>Квадратный контейнер точек-маркеров, наложенный ровно на RawImage (тот же размер).</summary>
        [SerializeField]
        private RectTransform markerArea;

        /// <summary>Префаб точки-маркера сущности (UI Image). Пулится по числу видимых сущностей.</summary>
        [SerializeField]
        private GameObject entityMarkerPrefab;

        /// <summary>Точка игрока — всегда в центре мини-карты (камера центрирована на игроке).</summary>
        [SerializeField]
        private Image playerMarker;

        /// <summary>
        /// Множитель охвата радара относительно основной камеры: minimapSize = mainCamera.size × factor.
        /// Дефолт 2 — радар видит вдвое дальше по стороне (площадь ∝ size², т.е. вчетверо по площади).
        /// Подбирается в инспекторе.
        /// </summary>
        [SerializeField]
        private float minimapZoomFactor = 2f;

        /// <summary>Z камеры-радара (как у основной 2D-камеры — чтобы слои тайлов попадали в кадр).</summary>
        private const float CAMERA_Z = -10f;

        /// <summary>Пул точек сущностей (переиспользуем, не пересоздаём каждый кадр — паттерн боевого текста/слотов).</summary>
        private readonly List<GameObject> _markerPool = new List<GameObject>();

        /// <summary>Видима ли мини-карта (toggle-клавиша N).</summary>
        private bool _minimapVisible = true;

        protected override void Awake()
        {
            if (minimapRoot == null)
            {
                Error("Мини-карта: не присвоена панель minimapRoot");
                return;
            }

            if (minimapCamera == null)
            {
                Error("Мини-карта: не присвоена камера minimapCamera");
                return;
            }

            if (mainCamera == null)
            {
                Error("Мини-карта: не присвоена основная камера mainCamera");
                return;
            }

            if (markerArea == null)
            {
                Error("Мини-карта: не присвоен контейнер маркеров markerArea");
                return;
            }

            if (entityMarkerPrefab == null)
            {
                Error("Мини-карта: не присвоен префаб точки entityMarkerPrefab");
                return;
            }

            if (playerMarker == null)
            {
                Error("Мини-карта: не присвоена точка игрока playerMarker");
                return;
            }

            // Ортокамеру фиксируем кодом; охват (orthographicSize) привязан к основной камере и
            // пересчитывается в Update — её размер может меняться в рантайме (под разрешение/aspect).
            minimapCamera.orthographic = true;

            base.Awake();
        }

        protected override void Update()
        {
            base.Update();

            // Toggle. Keybinds (#9) отложены — клавиша захардкожена, как остальные хоткеи (I/M/Escape в UIController).
            if (Input.GetKeyDown(KeyCode.N))
            {
                _minimapVisible = !_minimapVisible;
                minimapRoot.SetActive(_minimapVisible);
                minimapCamera.enabled = _minimapVisible;   // скрытую карту не рендерим в RenderTexture (экономия)
            }

            if (!_minimapVisible)
                return;

            // Охват радара = основная камера × коэффициент. Читаем её orthographicSize КАЖДЫЙ кадр
            // (может меняться в рантайме), не кешируем.
            minimapCamera.orthographicSize = mainCamera.orthographicSize * minimapZoomFactor;

            // Игрок ещё не заспавнен (до /load) — прятать все маркеры, включая центральную точку.
            if (player == null)
            {
                HideAllMarkers();
                return;
            }

            // Камера-радар следует за игроком. Берём transform.position (сглаженная визуальная позиция —
            // ровно то, что рендерит основная камера), чтобы фон мини-карты совпадал с большим видом.
            Vector3 playerPos = player.transform.position;
            minimapCamera.transform.position = new Vector3(playerPos.x, playerPos.y, CAMERA_Z);

            UpdateMarkers(playerPos);
        }

        /// <summary>
        /// Перерисовывает точки: игрок — в центре, остальные сущности мира — по разнице мировых позиций.
        /// </summary>
        private void UpdateMarkers(Vector3 playerPos)
        {
            // Пиксель markerArea на мировой юнит. markerArea квадратный и совпадает с RawImage, а тот
            // показывает квадратный RenderTexture камеры с охватом 2×RADIUS юнитов → полу-сторона области
            // (в пикселях) соответствует RADIUS юнитам.
            float halfPx = markerArea.rect.height * 0.5f;
            if (halfPx <= 0f)
                return;   // layout ещё не посчитан (первый кадр) — пропускаем, отрисуем в следующем
            // Масштаб — от АКТУАЛЬНОГО размера радара (Update уже пересчитал его от основной камеры),
            // иначе точки разъедутся с фоном при новом зуме.
            float pixelsPerUnit = halfPx / minimapCamera.orthographicSize;

            // Игрок всегда в центре.
            if (!playerMarker.gameObject.activeSelf)
                playerMarker.gameObject.SetActive(true);
            playerMarker.rectTransform.anchoredPosition = Vector2.zero;

            int used = 0;
            foreach (Transform mapZone in worldObject.transform)
            {
                foreach (Transform entityTransform in mapZone)
                {
                    EntityModel model = entityTransform.GetComponent<EntityModel>();
                    if (model == null)
                        continue;
                    if (model == player)
                        continue;                               // игрок — отдельная центральная точка
                    if (model.action == ACTION_REMOVE)
                        continue;                               // удаляемых с карты не рисуем

                    Vector3 delta = entityTransform.position - playerPos;
                    Vector2 markerPos = new Vector2(delta.x, delta.y) * pixelsPerUnit;

                    // Вне круга радара — за границей видимой области — не показываем.
                    if (markerPos.magnitude > halfPx)
                        continue;

                    GameObject marker = GetPooledMarker(used++);
                    marker.GetComponent<RectTransform>().anchoredPosition = markerPos;
                    marker.GetComponent<Image>().color = MarkerColor(model.type);
                }
            }

            // Лишние точки из пула — спрятать.
            for (int i = used; i < _markerPool.Count; i++)
                if (_markerPool[i].activeSelf)
                    _markerPool[i].SetActive(false);
        }

        /// <summary>Прячет все точки (нет игрока / карта выключена).</summary>
        private void HideAllMarkers()
        {
            if (playerMarker.gameObject.activeSelf)
                playerMarker.gameObject.SetActive(false);
            for (int i = 0; i < _markerPool.Count; i++)
                if (_markerPool[i].activeSelf)
                    _markerPool[i].SetActive(false);
        }

        /// <summary>Точка пула по индексу (доращивает пул при нехватке, включает скрытую).</summary>
        private GameObject GetPooledMarker(int index)
        {
            while (_markerPool.Count <= index)
                _markerPool.Add(Instantiate(entityMarkerPrefab, markerArea));

            if (!_markerPool[index].activeSelf)
                _markerPool[index].SetActive(true);

            return _markerPool[index];
        }

        /// <summary>Цвет точки по типу сущности (kind с сервера).</summary>
        private static Color MarkerColor(string type)
        {
            switch (type)
            {
                case "enemy":  return new Color(0.90f, 0.20f, 0.20f);   // красный — враги
                case "player": return new Color(0.30f, 0.70f, 1.00f);   // голубой — другие игроки
                case "animal": return new Color(0.95f, 0.85f, 0.30f);   // жёлтый — животные
                default:       return new Color(0.75f, 0.75f, 0.75f);   // серый — объекты и прочее
            }
        }
    }
}
