using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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

        /// <summary>
        /// Размер точки-метки в КЛЕТКАХ карты — единственное число, задающее величину точек на ОБОИХ
        /// показах: у радара и у обзорной карты мира. Каждый показ переводит клетки в свои пиксели своим
        /// масштабом (радар — пикселями панели на клетку охвата камеры, обзорная — масштабом вписывания
        /// раскладки в окно), потому в пикселях его задать нельзя: пиксель у показов разный, клетка общая.
        /// Точка от этого перестаёт быть постоянной в пикселях экрана и следует за масштабом фона — она
        /// отмечает место на карте, а не наклеена поверх неё.
        ///
        /// Значение выведено из прежнего размера метки радара: точка держалась в сцене размером 10.4
        /// пикселя панели, а при фактическом охвате радара на клетку приходится 5.88 пикселя панели —
        /// частное этих двух чисел и стоит здесь, отчего вид радара и остался прежним. Охват задают
        /// <see cref="minimapZoomFactor"/> и обзор камеры, а обзор камеры считается из присланного
        /// сервером радиуса жизни игрока и соотношения сторон экрана (см. CameraController).
        /// </summary>
        protected const float MARKER_TILES = 1.768f;

        /// <summary>
        /// Во сколько раз своя точка крупнее чужой — тоже общее обоим показам: своё место игрок должен
        /// находить сразу. Отношение, а не второй размер: иначе величина точек подбиралась бы двумя
        /// числами и разница между своей меткой и чужими уезжала бы от каждой правки.
        /// </summary>
        protected const float PLAYER_MARKER_SCALE = 1.5f;

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

            // Разобранные цвета меток — снимок справочника компонентов и каталога префабов той игры, в
            // которую вошли: со сменой игры значения другие, а статика вход переживает.
            _markerColors.Clear();

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

            // Шапка текущей карты — общий вход обоих рисующих методов (фон и точки): берём ОДИН раз на
            // кадр, а не по разу в каждом — второй проход по тому же словарю карт был бы параллельным
            // обходом тех же данных ради того же вопроса (интерьер это или мозаика открытого мира).
            Dictionary<int, TileCacheService.CachedMap> maps = TileCacheService.GetWorldMaps(GAME_ID, ConnectController.world);
            maps.TryGetValue(player.map, out TileCacheService.CachedMap current);

            UpdateMinimapMaps(playerPos, maps, current);
            UpdateMarkers(playerPos, current);
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
        /// Точка метки заданного цвета: светлое тело в тёмной кайме. Кайма и есть суть — одноцветная
        /// точка сливается то с песком, то с водой, то с крышами, а обведённая читается на любом фоне,
        /// потому обведены ВСЕ точки радара, не только своя. Рисуется кодом, отдельного ассета не просит;
        /// цветов у радара считанные единицы, потому нарисованное держим готовым.
        /// </summary>
        protected static Sprite MarkerSprite(Color body)
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
        private void UpdateMinimapMaps(Vector3 playerPos, Dictionary<int, TileCacheService.CachedMap> maps, TileCacheService.CachedMap current)
        {
            // Шапка текущей карты ещё не легла в кеш (RememberMap пишет её по завершении скачивания JSON
            // карты) — до этого момента фон пустой.
            if (current == null)
            {
                foreach (KeyValuePair<int, RectTransform> shown in _minimapTiles)
                    shown.Value.gameObject.SetActive(false);
                return;
            }

            // Интерьер без раскладки открытого мира — единственная карта на радаре (см. фильтр в основном
            // цикле ниже): масштаб камеры/radius ей не подходит — у неё нет соседей и нет смысла держать
            // масштаб «как в открытом мире», она сама себе весь видимый сейчас мир и вправе вписаться в
            // панель целиком, а не занимать мелкий кусок в углу.
            if (!current.hasOpenworldPosition)
            {
                ReleaseStaleMinimapTiles(maps, player.map);
                DrawSoleMinimapTile();
                return;
            }

            // Пикселей панели на клетку карты: половина стороны области соответствует охвату радара,
            // а он привязан к основной камере (её размер меняется в рантайме — читаем каждый кадр).
            float halfPx = markerArea.rect.height * 0.5f;
            if (halfPx <= 0f)
                return;

            float radius = mainCamera.orthographicSize * minimapZoomFactor;
            float pixelsPerTile = halfPx / radius;

            // Где игрок во всём мире: место его карты в раскладке плюс его клетка внутри неё. Раскладка
            // считает клетки картинок карт (ось Y вниз), потому позиция сцены переводится в тот же счёт
            // (MapImageCell) — иначе фон уезжает от точки игрока на пол-клетки влево и вверх.
            Vector2 inMap = MapImageCell(playerPos);
            float playerWorldX = current.x + inMap.x;
            float playerWorldY = current.y + inMap.y;

            foreach (KeyValuePair<int, TileCacheService.CachedMap> pair in maps)
            {
                // Интерьер того же world, но не карта игрока сейчас, — в мозаике не участвует: несколько
                // интерьеров делят один world (CachedMap.hasOpenworldPosition), их x=0,y=0 условны и без
                // этого фильтра наложились бы друг на друга либо подменяли бы друг друга на радаре.
                if (!pair.Value.hasOpenworldPosition && pair.Key != player.map)
                {
                    if (_minimapTiles.TryGetValue(pair.Key, out RectTransform stale) && stale.gameObject.activeSelf)
                        stale.gameObject.SetActive(false);
                    continue;
                }

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
            ReleaseStaleMinimapTiles(maps, -1);
        }

        /// <summary>
        /// Снимает с панели картинки карт, которым на ней больше не место: при <paramref name="soleMapId"/>
        /// от нуля и выше остаётся только эта карта (радар интерьера показывает одну комнату), иначе — все
        /// карты набора <paramref name="maps"/> (ушедшие из него остались в прежнем мире).
        ///
        /// Именно снимает, а не гасит: нарисованную картинку (Texture2D, Sprite) сборщик мусора не трогает,
        /// её снимает только Destroy, а весит она тем больше, чем мельче карта — детальность считается от
        /// размера карты (<see cref="WorldMapRenderer"/>), и у комнаты картинка выходит в мегабайты. Десяток
        /// обойдённых комнат держал бы эти мегабайты до конца игры. Вернувшаяся карта создаётся заново из
        /// PNG в кеше на диске — тем же путём, что и в первый раз (<see cref="EnsureMinimapTile"/>).
        /// </summary>
        private void ReleaseStaleMinimapTiles(Dictionary<int, TileCacheService.CachedMap> maps, int soleMapId)
        {
            // Список нужен только на смену набора: словарь правится по его итогам, а во время обхода его
            // трогать нельзя. В обычном кадре снимать нечего, и он не создаётся вовсе.
            List<int> stale = null;

            foreach (KeyValuePair<int, RectTransform> shown in _minimapTiles)
            {
                bool keep = soleMapId >= 0 ? shown.Key == soleMapId : maps.ContainsKey(shown.Key);

                if (!keep)
                    (stale ??= new List<int>()).Add(shown.Key);
            }

            if (stale == null)
                return;

            foreach (int mapId in stale)
            {
                RectTransform tile = _minimapTiles[mapId];
                _minimapTiles.Remove(mapId);

                if (tile == null)
                    continue;

                Image image = tile.GetComponent<Image>();
                if (image != null && image.sprite != null)
                {
                    Destroy(image.sprite.texture);
                    Destroy(image.sprite);
                }

                Destroy(tile.gameObject);
            }
        }

        /// <summary>
        /// Рисует единственный тайл радара (интерьер без раскладки открытого мира), растягивая картинку
        /// комнаты на ВСЮ панель — по требованию: маленькая комната заполняет зону видимости радара целиком,
        /// пропорции НЕ сохраняются (contain здесь не нужен, искажение допустимо и ожидаемо). Позиция и
        /// размер — фиксированные константы панели, от позиции игрока не зависят: этот тайл не участвует
        /// в общей per-frame формуле открытого мира (ранний return в UpdateMinimapMaps не даёт основному
        /// циклу его коснуться) — иначе он либо не двигался бы синхронно с формулой, либо съезжал бы и
        /// вылезал за рамку панели при ходьбе. Прочие тайлы, оставшиеся от прежнего состояния (мозаика
        /// открытого мира либо другая посещённая комната), к этому моменту уже сняты
        /// (<see cref="ReleaseStaleMinimapTiles"/>) — на радаре интерьера им не место.
        /// </summary>
        private void DrawSoleMinimapTile()
        {
            RectTransform tile = EnsureMinimapTile(player.map);
            if (tile == null)
                return;

            if (!tile.gameObject.activeSelf)
                tile.gameObject.SetActive(true);

            // Pivot тайла — (0,1), левый верхний угол (общий с тайлами открытого мира, см. EnsureMinimapTile):
            // чтобы растянутая на весь markerArea картинка легла по его центру, якорную точку смещаем в
            // левый верхний угол области (половина размера влево и вверх от центра).
            Vector2 size = markerArea.rect.size;
            tile.sizeDelta = size;
            tile.anchoredPosition = new Vector2(-size.x / 2f, size.y / 2f);
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
        /// Перерисовывает точки. Способ считать позицию зависит от того, что сейчас несёт фон радара
        /// (<see cref="UpdateMinimapMaps"/>):
        /// — мозаика открытого мира панорамируется вокруг игрока → игрок неподвижен в центре панели,
        ///   все прочие точки — по разнице их мировой позиции и позиции игрока;
        /// — одиночный интерьер (<see cref="DrawSoleMinimapTile"/>) закреплён и растянут на всю панель →
        ///   наоборот, ПОЛОЖЕНИЕ игрока внутри комнаты решает, куда лечь его точке (в центре панели он
        ///   только тогда, когда физически стоит в центре комнаты), а точки прочих сущностей кладутся по
        ///   их СОБСТВЕННЫМ координатам той же трансформацией — неподвижно относительно фона, когда идёт
        ///   сам игрок. Общая часть обеих веток вынесена в <paramref name="mapper"/> (мировая позиция →
        ///   пиксель панели относительно её центра) — циклы по сущностям и переходам не дублируются.
        /// </summary>
        private void UpdateMarkers(Vector3 playerPos, TileCacheService.CachedMap current)
        {
            Vector2 panelSize = markerArea.rect.size;
            if (panelSize.x <= 0f || panelSize.y <= 0f)
                return;   // layout ещё не посчитан (первый кадр) — пропускаем, отрисуем в следующем

            System.Func<Vector3, Vector2> mapper;
            float? cullRadius;
            float markerSize;

            if (current != null && !current.hasOpenworldPosition)
            {
                // Интерьер: фон закреплён и растянут по обеим осям независимо (аспект комнаты не совпадает
                // с аспектом панели) — потому коэффициенты X/Y считаются РАЗДЕЛЬНО, тем же преобразованием,
                // что и картинка комнаты в DrawSoleMinimapTile (клетка карты → доля ширины/высоты → пиксель
                // от левого верхнего угла панели, минус её половина — перевод в систему координат маркеров,
                // где ноль есть центр markerArea). Клетку берём переводом MapImageCell: картинка считает
                // клетки от своего угла, сцена — от середины клетки и от ног сущности. Радиуса отсечения
                // нет: комната видна целиком, культить по кругу открытого мира здесь нечего.
                mapper = worldPos =>
                {
                    Vector2 cell = MapImageCell(worldPos);

                    return new Vector2(
                        -panelSize.x / 2f + cell.x / current.width  * panelSize.x,
                         panelSize.y / 2f - cell.y / current.height * panelSize.y
                    );
                };
                cullRadius = null;

                // Точка круглая, а панель интерьера растянута по осям РАЗДЕЛЬНО — размер выводим от одного
                // коэффициента, и взят коэффициент ВЫСОТЫ (пикселей панели на клетку комнаты по вертикали):
                // тем же измерением панели меряет себя ветка открытого мира ниже (halfPx — половина высоты),
                // потому точка не меняет величины при переходе из комнаты в открытый мир.
                markerSize = MARKER_TILES * panelSize.y / current.height;
            }
            else
            {
                // Открытый мир: фон панорамируется вокруг игрока — тем же выражением, что и в
                // UpdateMinimapMaps, иначе точки разъедутся с картинкой. Игрок неподвижен в центре, точки
                // вне круга охвата радара гасятся. Считается РАЗНОСТЬ позиций сцены, потому перевод в
                // клетки картинки (MapImageCell) здесь не нужен: он сдвигает обе точки одинаково и в
                // разности пропадает — с картинкой их совмещает сдвиг самого фона в UpdateMinimapMaps.
                float halfPx = panelSize.y * 0.5f;
                float pixelsPerUnit = halfPx / (mainCamera.orthographicSize * minimapZoomFactor);
                mapper = worldPos => new Vector2(worldPos.x - playerPos.x, worldPos.y - playerPos.y) * pixelsPerUnit;
                cullRadius = halfPx;
                markerSize = MARKER_TILES * pixelsPerUnit;
            }

            if (!playerMarker.gameObject.activeSelf)
                playerMarker.gameObject.SetActive(true);
            ApplyPlayerMarker(playerMarker, player.prefab);
            playerMarker.rectTransform.anchoredPosition = mapper(playerPos);
            // Размер меток радара считается здесь каждый кадр, а не берётся из сцены: охват камеры
            // меняется в рантайме, и пиксель панели на клетку вместе с ним.
            float ownSize = markerSize * PLAYER_MARKER_SCALE;
            playerMarker.rectTransform.sizeDelta = new Vector2(ownSize, ownSize);

            // Переходы — первыми: точки берутся из пула по порядку, и ранние ложатся в иерархии ниже.
            // Существо, стоящее на переходе, должно быть видно поверх него, а не наоборот.
            int used = DrawWarps(mapper, cullRadius, markerSize, 0);
            used = DrawGates(mapper, cullRadius, markerSize, used);

            foreach (Transform mapZone in worldObject.transform)
            {
                foreach (Transform entityTransform in mapZone)
                {
                    EntityModel model = entityTransform.GetComponent<EntityModel>();
                    if (model == null)
                        continue;
                    if (model == player)
                        continue;                               // игрок — отдельная точка, посчитана выше
                    if (model.action == ACTION_REMOVE)
                        continue;                               // удаляемых с карты не рисуем

                    Color? color = MarkerColor(model.prefab);
                    if (color == null)
                        continue;   // цвета метки у вида нет — контент игры его на картах не показывает

                    Vector2 markerPos = mapper(entityTransform.position);

                    if (cullRadius.HasValue && markerPos.magnitude > cullRadius.Value)
                        continue;   // вне круга радара — за границей видимой области

                    PlaceMarker(GetPooledMarker(used++), markerPos, MarkerSprite(color.Value), markerSize);
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
        /// игроку нужен ровно затем, чтобы дойти до него. Перевод мировой позиции в пиксель панели —
        /// забота <paramref name="mapper"/> (см. <see cref="UpdateMarkers"/>): режим (интерьер/открытый
        /// мир) сюда не просачивается, отсечение по кругу — только когда <paramref name="cullRadius"/> задан.
        /// </summary>
        private int DrawWarps(System.Func<Vector3, Vector2> mapper, float? cullRadius, float markerSize, int used)
        {
            foreach (Transform grid in mapObject.transform)
            {
                Transform warps = grid.Find(WarpMarker.LAYER);
                if (warps == null)
                    continue;   // игра переходами по разметке не пользуется либо карта ещё строится

                foreach (Transform warp in warps)
                {
                    // Место перехода берём у самой метки (WarpMarker.scene), а не её позицию: метка
                    // накрывает квадрат клетки нарисованного полотна, а точки панели считаются в
                    // координатах сцены — тех же, в которых сюда приходят игрок и сущности.
                    Vector2 markerPos = mapper(warp.GetComponent<WarpMarker>().scene);

                    if (cullRadius.HasValue && markerPos.magnitude > cullRadius.Value)
                        continue;   // за границей видимой области радара

                    PlaceMarker(GetPooledMarker(used++), markerPos, MarkerSprite(WARP_COLOR), markerSize);
                }
            }

            return used;
        }

        /// <summary>
        /// Метки проходов в недоступные соседние карты (<see cref="MapController.getGates"/>): крест на
        /// каждом свободном участке общей границы — ровно там, где игрок упрётся в невидимую стену, пробуя
        /// перейти. Считает их сам MapController: разметка карт и доступность соседей — его данные, а не
        /// показа. Перевод в пиксель панели — общий <paramref name="mapper"/> (см. <see cref="UpdateMarkers"/>).
        ///
        /// Метка на проход одна, в его середине (<see cref="MapController.Gate.center"/>): на радаре карта
        /// сжата в панель, и поклеточные метки слились бы в неразличимую полосу. По всей ширине прохода
        /// метки стоят там, где места хватает, — на самой земле (<see cref="GateController"/>).
        /// </summary>
        private int DrawGates(System.Func<Vector3, Vector2> mapper, float? cullRadius, float markerSize, int used)
        {
            foreach (Gate gate in getGates())
            {
                Vector2 markerPos = mapper(gate.center);

                if (cullRadius.HasValue && markerPos.magnitude > cullRadius.Value)
                    continue;   // за границей видимой области радара

                PlaceMarker(GetPooledMarker(used++), markerPos, UnavailableSprite(), markerSize);
            }

            return used;
        }

        /// <summary>
        /// Ставит метку на место. Вид её несёт сам рисунок, а не оттенок картинки: кайма у всех меток одна
        /// и та же тёмная, у точки красится только тело — иначе обводка красилась бы вместе с ним и пропадала.
        /// Размер приходит вычисленным (<see cref="MARKER_TILES"/> в пикселях панели), а не берётся из
        /// префаба точки: он зависит от охвата камеры и меняется в рантайме вместе с ним.
        /// </summary>
        private static void PlaceMarker(GameObject marker, Vector2 position, Sprite sprite, float size)
        {
            RectTransform rect = marker.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);

            Image image = marker.GetComponent<Image>();
            image.sprite = sprite;
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
        /// Цвет точки перехода. Переход — не сущность, а разметка карты: своего значения в контенте у него
        /// нет, и цвет задаёт клиент — это единственная метка карт, оттенок которой клиент назначает сам.
        /// Взят белый: точки различаются только цветом (формы у них одинаковые), а цвета сущностей задаёт
        /// контент игры, и любой цветной оттенок он вправе занять под свой вид — белый же оставлен разметке.
        /// </summary>
        protected static readonly Color WARP_COLOR = Color.white;

        /// <summary>Компонент, несущий цвет метки сущности на картах клиента.</summary>
        private const string COMPONENT_MAP_COLOR = "map_color";

        /// <summary>
        /// Разобранный цвет метки по prefab'у сущности: разбор строки и жалоба на негодное значение идут
        /// по разу на prefab, а не на каждую точку каждого кадра. Сбрасывается при входе в игру (Awake).
        /// Пустое значение — у этого prefab'а метки нет вовсе (см. <see cref="MarkerColor"/>).
        /// </summary>
        private static readonly Dictionary<string, Color?> _markerColors = new Dictionary<string, Color?>();

        /// <summary>
        /// Цвет метки сущности: значение компонента цвета, разрешённое обычной цепочкой «заданное prefab'у →
        /// умолчание компонента» (<see cref="AnimationCacheService.GetComponentValue"/>).
        /// Оно же решает, показана ли сущность на картах ВООБЩЕ: есть значение — метка есть, нет значения —
        /// метки нет. Отбор целиком лежит в контенте игры, потому вида сущности этот код не спрашивает
        /// вовсе: у другой игры виды свои, а правка отбора либо цвета не должна стоить пересборки клиента.
        /// Пусто — значения нет (компонент виду не положен) либо оно записано не как #RRGGBB.
        /// </summary>
        protected static Color? MarkerColor(string prefab)
        {
            string key = prefab ?? string.Empty;

            if (_markerColors.TryGetValue(key, out Color? known))
                return known;

            Color? color = null;
            string hex = AnimationCacheService.GetComponentValue(prefab, COMPONENT_MAP_COLOR, null)?.Value<string>();

            if (!string.IsNullOrEmpty(hex))
            {
                if (ColorUtility.TryParseHtmlString(hex, out Color parsed))
                    color = parsed;
                else
                    Debug.LogError("Карты: цвет метки «" + hex + "» у prefab'а " + prefab + " записан не как #RRGGBB");
            }

            _markerColors[key] = color;
            return color;
        }

        /// <summary>
        /// Метка своего игрока — общая обоим показам, радару и обзорной карте: рисунок берётся по цвету
        /// его prefab'а из контента игры (<see cref="MarkerColor"/>), тем же порядком, что и у прочих
        /// сущностей. От остальных меток своя отличается только размером — общим множителем
        /// <see cref="PLAYER_MARKER_SCALE"/> поверх <see cref="MARKER_TILES"/>, который каждый показ
        /// переводит в свои пиксели.
        /// Правило показа тоже общее: цвета у вида нет — контент игры его на картах не показывает, и своя
        /// метка гаснет наравне с чужими.
        /// </summary>
        protected static void ApplyPlayerMarker(Image marker, string prefab)
        {
            Color? color = MarkerColor(prefab);

            if (color == null)
            {
                marker.gameObject.SetActive(false);
                return;
            }

            marker.sprite = MarkerSprite(color.Value);
            marker.color = Color.white;   // цвет несёт спрайт
        }

        /// <summary>Сторона текстуры креста. Крест — метка размером с точку, мелких деталей в нём нет.</summary>
        private const int UNAVAILABLE_TEXTURE_SIZE = 64;

        /// <summary>Нарисованный крест — общий на все карты обоих показов.</summary>
        private static Sprite _unavailableSprite;

        /// <summary>
        /// Метка прохода в недоступную локацию: красный крест в тёмной кайме. Кайма — та же, что у точек, и
        /// по той же причине: карты пёстрые, одноцветная линия на них теряется. Формой крест отличается от
        /// круглых точек — на карте метки различают только по виду. Рисуется кодом, ассета не просит.
        /// </summary>
        protected static Sprite UnavailableSprite()
        {
            // Живость проверяем обязательно: статика переживает остановку игры, а сама картинка рисуется в
            // рантайме и уничтожается вместе с ней (тот же случай, что у точек в MarkerSprite).
            if (_unavailableSprite != null)
                return _unavailableSprite;

            Texture2D texture = new Texture2D(UNAVAILABLE_TEXTURE_SIZE, UNAVAILABLE_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            Color body = new Color(0.90f, 0.15f, 0.15f);
            Color border = new Color(0.05f, 0.05f, 0.08f);

            // Полутолщина полосы и каймы — в долях стороны: метка мелкая (с точку сущности), и в пикселях
            // толщина зависит от того, какого размера её рисуют на панели.
            const float half = 0.10f;
            const float edge = 0.17f;

            for (int y = 0; y < UNAVAILABLE_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < UNAVAILABLE_TEXTURE_SIZE; x++)
                {
                    float u = x / (float)(UNAVAILABLE_TEXTURE_SIZE - 1);
                    float v = y / (float)(UNAVAILABLE_TEXTURE_SIZE - 1);

                    // Расстояние до каждой из двух диагоналей квадрата (делитель — длина нормали).
                    float distance = Mathf.Min(Mathf.Abs(u - v), Mathf.Abs(u + v - 1f)) / Mathf.Sqrt(2f);

                    Color color;
                    if (distance <= half)
                        color = body;
                    else if (distance <= edge)
                        color = border;
                    else
                        color = new Color(0f, 0f, 0f, 0f);

                    // Край каймы сглаживаем по последней доле, иначе у мелкой метки он выходит ступенчатым
                    // (та же причина, что у края точки в MarkerSprite).
                    if (distance > edge - 0.02f && distance <= edge)
                        color.a = Mathf.Clamp01((edge - distance) / 0.02f);

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();

            _unavailableSprite = Sprite.Create(texture, new Rect(0, 0, UNAVAILABLE_TEXTURE_SIZE, UNAVAILABLE_TEXTURE_SIZE), new Vector2(0.5f, 0.5f));
            return _unavailableSprite;
        }
    }
}
