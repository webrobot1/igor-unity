using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Клиентская мини-карта (радар вокруг игрока). Сервер её НЕ шлёт — всё берётся из локального кеша карт
    /// и живого мира (worldObject).
    ///
    /// Фон — нарисованные картинки карт (<see cref="WorldMapRenderer"/>, кеш ведёт TileCacheService), те же,
    /// что показывает обзорная карта мира: карты кладутся по своим местам в открытом мире, а контейнер
    /// сдвигается так, чтобы игрок был в середине панели. Прежде фон рисовала вторая камера над реальными
    /// тайлами — картинка выходила та же, а стоила почти половины кадра (замер: 18,5 мс с ней против 9,8 мс
    /// без неё), потому от неё отказались.
    ///
    /// Маркеры сущностей — UI-точки поверх (<see cref="entityMarkerPrefab"/>), позиция считается как
    /// разница МИРОВЫХ позиций (сущность − игрок) × масштаб. Фон и точки живут в одном масштабе: обоим
    /// половина стороны панели соответствует охвату радара, потому точка над сущностью ложится ровно туда
    /// же, где сущность видна в основном окне относительно игрока. Тем же порядком показаны переходы на
    /// другие карты — они не сущности, а разметка самой карты (<see cref="DrawWarps"/>).
    /// </summary>
    abstract public class MinimapController : PlayerController
    {
        [Header("Мини-карта (радар)")]

        /// <summary>
        /// UI-панель мини-карты целиком (RawImage + рамка). Её активность И ЕСТЬ состояние мини-карты —
        /// отдельного флага видимости нет: источник — настройка игрока (компонент settings, ключ minimap).
        /// </summary>
        [SerializeField]
        private GameObject minimapRoot;

        /// <summary>
        /// Контейнер картинок карт под точками. Фон радара — те же нарисованные миниатюры карт, что и у
        /// обзорной карты мира (кеш ведёт TileCacheService): карты кладутся сюда по своим местам в открытом
        /// мире, а контейнер сдвигается так, чтобы игрок был в середине панели.
        /// </summary>
        [SerializeField]
        private RectTransform minimapMaps;

        /// <summary>Префаб картинки одной карты — тот же, что у обзорной карты мира.</summary>
        [SerializeField]
        private GameObject minimapMapPrefab;

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
        /// Подпись с названием текущей карты под панелью радара. Название говорит игроку, где он
        /// находится, — нужно в обычной игре, а не только в тестовом режиме, поэтому висит у радара,
        /// а не в служебном блоке счётчиков (там остаётся только номер карты — адрес для инструментов).
        /// </summary>
        [SerializeField]
        private Text mapNameLabel;

        /// <summary>
        /// Множитель охвата радара относительно основной камеры: minimapSize = mainCamera.size × factor.
        /// Дефолт 2 — радар видит вдвое дальше по стороне (площадь ∝ size², т.е. вчетверо по площади).
        /// Подбирается в инспекторе.
        /// </summary>
        [SerializeField]
        private float minimapZoomFactor = 2f;

        /// <summary>Сторона текстуры метки игрока — общей у радара и обзорной карты.</summary>
        private const int MARKER_TEXTURE_SIZE = 32;

        /// <summary>Пул точек сущностей (переиспользуем, не пересоздаём каждый кадр — паттерн боевого текста/слотов).</summary>
        private readonly List<GameObject> _markerPool = new List<GameObject>();

        /// <summary>Нарисованные точки по цвету тела: рисунок один, цветов у радара считанные единицы.</summary>
        private static readonly Dictionary<Color, Sprite> _markerSprites = new Dictionary<Color, Sprite>();

        /// <summary>Уже показанные картинки карт (id карты → её место на панели): создаются один раз на карту.</summary>
        private readonly Dictionary<int, RectTransform> _minimapTiles = new Dictionary<int, RectTransform>();

        /// <summary>Идёт отрисовка картинки карты — вторую одновременно не начинаем.</summary>
        private bool _minimapRendering;

        protected override void Awake()
        {
            if (minimapRoot == null)
            {
                Error("Мини-карта: не присвоена панель minimapRoot");
                return;
            }

            if (minimapMaps == null)
            {
                Error("Мини-карта: не присвоен контейнер картинок карт minimapMaps");
                return;
            }

            if (minimapMapPrefab == null)
            {
                Error("Мини-карта: не присвоен префаб картинки карты minimapMapPrefab");
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

            if (mapNameLabel == null)
            {
                Error("Мини-карта: не присвоена подпись названия карты mapNameLabel");
                return;
            }

            // Метка игрока — та же, что на обзорной карте (жёлтая точка в тёмной кайме): белая точка на
            // пёстром фоне терялась, а искать себя игрок должен взглядом, не приглядываясь. Один вид метки
            // на оба показа — и узнаётся сразу, и правится в одном месте.
            playerMarker.sprite = BuildPlayerMarkerSprite();
            playerMarker.color = Color.white;   // цвет несёт спрайт

            base.Awake();
        }

        protected override void Update()
        {
            base.Update();

            if (!minimapRoot.activeSelf)
                return;

            // Игрок ещё не заспавнен (до /load) — прятать все маркеры, включая центральную точку.
            if (player == null)
            {
                HideAllMarkers();
                return;
            }

            // Берём transform.position (сглаженная визуальная позиция — ровно то, что рендерит основная
            // камера), чтобы фон радара совпадал с большим видом.
            Vector3 playerPos = player.transform.position;

            UpdateMinimapMaps(playerPos);
            UpdateMarkers(playerPos);
        }

        /// <summary>
        /// Применяет настройку игрока «Мини-карта» (компонент settings, ключ minimap) — приходит с сервера.
        /// </summary>
        protected void SetMinimapEnabled(bool enabled)
        {
            minimapRoot.SetActive(enabled);

            if (!enabled)
                HideAllMarkers();
        }

        /// <summary>
        /// Название текущей карты в подписи радара. Пустая строка — карта ещё не загружена, названия нет.
        /// </summary>
        protected void SetMapName(string name)
        {
            if (mapNameLabel.text != name)
                mapNameLabel.text = name;
        }

        /// <summary>
        /// Метка игрока: светлая точка в тёмной кайме. Цвет тела — тёплый жёлтый, такого на картах
        /// почти нет. Ею же помечен игрок на обзорной карте мира — метка одна на оба показа.
        /// </summary>
        protected static Sprite BuildPlayerMarkerSprite()
        {
            return MarkerSprite(new Color(1f, 0.92f, 0.30f));
        }

        /// <summary>
        /// Точка метки заданного цвета: светлое тело в тёмной кайме. Кайма и есть суть — одноцветная
        /// точка сливается то с песком, то с водой, то с крышами, а обведённая читается на любом фоне,
        /// потому обведены ВСЕ точки радара, не только своя. Рисуется кодом, отдельного ассета не просит;
        /// цветов у радара считанные единицы, потому нарисованное держим готовым.
        /// </summary>
        private static Sprite MarkerSprite(Color body)
        {
            // Живость проверяем обязательно: словарь статический и переживает остановку игры, а сами
            // картинки рисуются в рантайме и уничтожаются вместе с ней — иначе следующий запуск берёт
            // из кеша уничтоженную картинку и точки рисуются белыми квадратами.
            if (_markerSprites.TryGetValue(body, out Sprite known) && known != null)
                return known;

            Texture2D texture = new Texture2D(MARKER_TEXTURE_SIZE, MARKER_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            float center = (MARKER_TEXTURE_SIZE - 1) / 2f;
            float outer = MARKER_TEXTURE_SIZE * 0.46f;   // внешний край каймы
            float inner = MARKER_TEXTURE_SIZE * 0.30f;   // где кайма переходит в тело метки

            Color border = new Color(0.05f, 0.05f, 0.08f);

            for (int y = 0; y < MARKER_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < MARKER_TEXTURE_SIZE; x++)
                {
                    float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));

                    Color color;
                    if (distance <= inner)
                        color = body;
                    else if (distance <= outer)
                        color = border;
                    else
                        color = new Color(0f, 0f, 0f, 0f);

                    // Край сглаживаем по последнему полупикселю, иначе кружок выходит ступенчатым.
                    if (distance > outer - 1f && distance <= outer)
                        color.a = Mathf.Clamp01(outer - distance);

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, MARKER_TEXTURE_SIZE, MARKER_TEXTURE_SIZE), new Vector2(0.5f, 0.5f));
            _markerSprites[body] = sprite;

            return sprite;
        }

        /// <summary>
        /// Держит фон радара: картинки карт из кеша, сдвинутые так, чтобы игрок оказался в середине панели.
        /// Прежде фон рисовала вторая камера над реальными тайлами — она стоила почти половины кадра
        /// (замер: 18,5 мс с ней против 9,8 мс без неё), а показывала то же самое.
        ///
        /// Карта, картинки которой ещё нет, рисуется по одной за раз в фоне: рисование стоит сборки целой
        /// карты, и пачкой оно подвесило бы игру. До готовности место карты остаётся пустым.
        /// </summary>
        private void UpdateMinimapMaps(Vector3 playerPos)
        {
            Dictionary<int, TileCacheService.CachedMap> maps = TileCacheService.GetWorldMaps(GAME_ID, ConnectController.world);

            // Карта игрока не размещена в открытом мире (интерьер, подземелье) — раскладки нет, фон пустой.
            if (!maps.TryGetValue(player.map, out TileCacheService.CachedMap current))
            {
                foreach (KeyValuePair<int, RectTransform> shown in _minimapTiles)
                    shown.Value.gameObject.SetActive(false);
                return;
            }

            // Пикселей панели на клетку карты: половина стороны области соответствует охвату радара,
            // а он привязан к основной камере (её размер меняется в рантайме — читаем каждый кадр).
            float halfPx = markerArea.rect.height * 0.5f;
            if (halfPx <= 0f)
                return;

            float radius = mainCamera.orthographicSize * minimapZoomFactor;
            float pixelsPerTile = halfPx / radius;

            // Где игрок во всём мире: место его карты плюс он внутри неё (внутри карты ось Y вверх,
            // а раскладка мира считает вниз — отсюда знак).
            float playerWorldX = current.x + playerPos.x;
            float playerWorldY = current.y - playerPos.y;

            foreach (KeyValuePair<int, TileCacheService.CachedMap> pair in maps)
            {
                RectTransform tile = EnsureMinimapTile(pair.Key);
                if (tile == null)
                    continue;

                // Углы округляем до целого пикселя — против субпиксельной щели на стыке соседних карт
                // (разбор — у обзорной карты, там же почему нельзя растягивать картинку с запасом).
                Vector2 position = new Vector2(
                    Mathf.Round( (pair.Value.x - playerWorldX) * pixelsPerTile),
                    Mathf.Round(-(pair.Value.y - playerWorldY) * pixelsPerTile)
                );
                Vector2 size = new Vector2(
                    Mathf.Round( (pair.Value.x + pair.Value.width  - playerWorldX) * pixelsPerTile) - position.x,
                    position.y - Mathf.Round(-(pair.Value.y + pair.Value.height - playerWorldY) * pixelsPerTile)
                );

                // Карта целиком вне панели — гасим: в мире их десятки, а видно от силы четыре. Рисовать
                // остальные значит гонять большие картинки мимо кадра (маска их обрежет уже ПОСЛЕ отрисовки).
                bool visible = position.x < halfPx && position.x + size.x > -halfPx
                            && position.y > -halfPx && position.y - size.y < halfPx;

                if (tile.gameObject.activeSelf != visible)
                    tile.gameObject.SetActive(visible);

                if (!visible)
                    continue;

                tile.sizeDelta = size;
                tile.anchoredPosition = position;
            }

            // Карты, ушедшие из набора (игрок сменил мир), с панели убираем.
            foreach (KeyValuePair<int, RectTransform> shown in _minimapTiles)
                if (!maps.ContainsKey(shown.Key) && shown.Value.gameObject.activeSelf)
                    shown.Value.gameObject.SetActive(false);
        }

        /// <summary>
        /// Картинка карты на панели: берётся из кеша, при первом показе создаётся. Картинки ещё нет —
        /// ставит карту в очередь на отрисовку и возвращает null (в этом кадре её просто не видно).
        /// </summary>
        private RectTransform EnsureMinimapTile(int mapId)
        {
            if (_minimapTiles.TryGetValue(mapId, out RectTransform known))
                return known;

            byte[] png = TileCacheService.GetWorldMapImage(GAME_ID, mapId);
            if (png == null)
            {
                if (!_minimapRendering)
                    StartCoroutine(RenderMinimapTile(mapId));

                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(png);
            // Без сглаживания — см. обзорную карту: фильтрация подмешивает прозрачность из-за края карты,
            // и стык соседних карт читается тёмной полосой.
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            GameObject tile = Instantiate(minimapMapPrefab, minimapMaps);
            tile.name = mapId.ToString();
            tile.GetComponent<Image>().sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);

            RectTransform rect = tile.GetComponent<RectTransform>();
            // Якорь — середина панели: раскладка считается от игрока, который стоит ровно в центре.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);

            _minimapTiles[mapId] = rect;
            return rect;
        }

        /// <summary>
        /// Рисует картинку одной карты и кладёт её в кеш. По одной за раз: рисование стоит сборки карты
        /// целиком, и параллельные заходы отняли бы кадры у самой игры.
        /// </summary>
        private IEnumerator RenderMinimapTile(int mapId)
        {
            _minimapRendering = true;
            yield return null;   // отдаём кадр: рисование пойдёт следующим, не в середине текущего

            byte[] png = WorldMapRenderer.Render(GAME_ID, mapId);
            if (png != null)
                TileCacheService.SaveWorldMapImage(GAME_ID, mapId, png);

            _minimapRendering = false;
        }

        /// <summary>
        /// Перерисовывает точки: игрок — в центре, остальные сущности мира — по разнице мировых позиций.
        /// </summary>
        private void UpdateMarkers(Vector3 playerPos)
        {
            // Пиксель markerArea на мировой юнит: полу-сторона квадратной области соответствует охвату
            // радара в юнитах (тайл = юнит).
            float halfPx = markerArea.rect.height * 0.5f;
            if (halfPx <= 0f)
                return;   // layout ещё не посчитан (первый кадр) — пропускаем, отрисуем в следующем
            // Охват берём от АКТУАЛЬНОГО размера основной камеры (он меняется в рантайме) — тем же
            // выражением, что и фон карт, иначе точки разъедутся с картинкой.
            float pixelsPerUnit = halfPx / (mainCamera.orthographicSize * minimapZoomFactor);

            // Игрок всегда в центре.
            if (!playerMarker.gameObject.activeSelf)
                playerMarker.gameObject.SetActive(true);
            playerMarker.rectTransform.anchoredPosition = Vector2.zero;

            // Переходы — первыми: точки берутся из пула по порядку, и ранние ложатся в иерархии ниже.
            // Существо, стоящее на переходе, должно быть видно поверх него, а не наоборот.
            int used = DrawWarps(playerPos, pixelsPerUnit, halfPx, 0);

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

                    PlaceMarker(GetPooledMarker(used++), markerPos, MarkerColor(model.type));
                }
            }

            // Лишние точки из пула — спрятать.
            for (int i = used; i < _markerPool.Count; i++)
                if (_markerPool[i].activeSelf)
                    _markerPool[i].SetActive(false);
        }

        /// <summary>
        /// Точки переходов на другие карты. Сущности под переходом нет — переход исполняет сама разметка
        /// карты (<see cref="WarpMarker"/>), потому точки берутся из уже построенного слоя меток: свечение
        /// на земле и точка на радаре зажигаются от одного источника, второго разбора разметки радар не
        /// заводит. Обходятся все карты в сцене — соседние тоже видны на радаре, а переход у их края
        /// игроку нужен ровно затем, чтобы дойти до него.
        /// </summary>
        private int DrawWarps(Vector3 playerPos, float pixelsPerUnit, float halfPx, int used)
        {
            foreach (Transform grid in mapObject.transform)
            {
                Transform warps = grid.Find(WarpMarker.LAYER);
                if (warps == null)
                    continue;   // игра переходами по разметке не пользуется либо карта ещё строится

                foreach (Transform warp in warps)
                {
                    Vector3 delta = warp.position - playerPos;
                    Vector2 markerPos = new Vector2(delta.x, delta.y) * pixelsPerUnit;

                    if (markerPos.magnitude > halfPx)
                        continue;   // за границей видимой области радара

                    PlaceMarker(GetPooledMarker(used++), markerPos, WARP_COLOR);
                }
            }

            return used;
        }

        /// <summary>
        /// Ставит точку на место и красит её. Цвет несёт нарисованная метка, а не оттенок картинки:
        /// кайма у всех точек одна и та же тёмная, красится только тело — иначе обводка красилась бы
        /// вместе с ним и пропадала.
        /// </summary>
        private static void PlaceMarker(GameObject marker, Vector2 position, Color body)
        {
            marker.GetComponent<RectTransform>().anchoredPosition = position;

            Image image = marker.GetComponent<Image>();
            image.sprite = MarkerSprite(body);
            image.color = Color.white;
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

        /// <summary>
        /// Цвет точки перехода. Взят зелёный: остальные заняты существами, а на радаре игрок различает
        /// точки только цветом — формы у них одинаковые.
        /// </summary>
        private static readonly Color WARP_COLOR = new Color(0.35f, 0.95f, 0.45f);

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
