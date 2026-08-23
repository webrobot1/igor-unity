using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Обзорная карта мира — вид всех карт текущего мира, которые игрок уже видел, разложенных так же, как
    /// они стоят в открытом мире. Сервер её НЕ шлёт и картинок не отдаёт: карты берутся из локального кеша
    /// (он копится сам — клиент качает карту, на которую заходит, и её смежные), а картинка каждой рисуется
    /// на месте из её же тайлов (<see cref="WorldMapRenderer"/>) и кладётся в кеш до правки карты.
    ///
    /// Мир у карт разный, а координаты открытого мира у каждого мира свои — потому берутся только карты мира
    /// игрока (он приходит при входе, <see cref="ConnectController.world"/>). Уход в интерьер либо подземелье
    /// меняет мир: раскладка там своя, и обзорная карта показывает уже её.
    ///
    /// Отличие от мини-карты (<see cref="MinimapController"/>): картинки карт у них общие, но радар держит
    /// ближайшее окружение игрока в масштабе игрового обзора и следует за ним, а здесь мир виден целиком —
    /// включая места, откуда игрок давно ушёл, — с увеличением и перетаскиванием. Оттого и показывает она
    /// само устройство мест — переходы и неподвижные сущности, — а не то, кто ходит по ним сейчас.
    ///
    /// Содержимое собирается на КАЖДОМ открытии окна вместе с раскладкой: за время, что окно закрыто, игрок
    /// успевает пройти новые карты, а сундук — смениться торговцем.
    /// </summary>
    abstract public class WorldMapController : InfoWindowController
    {
        [Header("Обзорная карта мира")]

        /// <summary>Окно обзорной карты целиком — открывается иконкой в панели и клавишей.</summary>
        [SerializeField]
        private CanvasGroup worldMapGroup;

        /// <summary>
        /// Область показа: её размер задаёт масштаб раскладки, а её маска обрезает то, что выходит за окно
        /// при увеличении и перетаскивании. Сама раскладка живёт в <see cref="worldMapContent"/> внутри неё.
        /// </summary>
        [SerializeField]
        private RectTransform worldMapArea;

        /// <summary>
        /// Контейнер раскладки: карты и точка игрока лежат тут. Увеличение и перетаскивание двигают ЕГО —
        /// раскладка при этом не пересчитывается, потому масштаб можно менять хоть каждый кадр.
        /// </summary>
        [SerializeField]
        private RectTransform worldMapContent;

        /// <summary>Префаб картинки одной карты (UI Image) — по экземпляру на карту мира из кеша.</summary>
        [SerializeField]
        private GameObject worldMapTilePrefab;

        /// <summary>Точка игрока поверх раскладки — где он сейчас во всём мире.</summary>
        [SerializeField]
        private Image worldMapPlayerMarker;

        /// <summary>Подпись окна — название мира (приходит при входе; своего справочника миров у клиента нет).</summary>
        [SerializeField]
        private Text worldMapTitle;

        /// <summary>
        /// Легенда под областью карты — какого цвета что на ней сейчас показано (см. <see cref="ShowLegend"/>).
        /// </summary>
        [SerializeField]
        private Text worldMapLegend;

        /// <summary>
        /// Глубина растушёвки в клетках, отсчитанная ВНУТРЬ мира от его внешнего контура: там мир обрывается
        /// на границе исследованного, и без растушёвки обрыв режет глаз. Меряется расстоянием до ближайшей
        /// клетки без карты, потому стык двух карт остаётся чистым сам — там мир продолжается.
        /// Держится короткой: у краёв карт лежат проходы и метки переходов, и глубокая пелена заслоняет
        /// их — сгладить обрыв нужно на самой кромке, а не затемнять вглубь заметную долю охвата.
        /// </summary>
        private const int FOG_EDGE_TILES = 6;

        /// <summary>
        /// Верхний предел растушёвки — доля МЕНЬШЕЙ стороны охвата мира: пелена противоположных сторон
        /// вместе съедает половину охвата, середина остаётся открытой. Без предела абсолютные клетки
        /// отнимали бы у маленького мира (интерьер, комната) долю охвата тем большую, чем он меньше,
        /// вплоть до заливки целиком: сквозь такую пелену рисунок читается хуже, чем сам обрыв края.
        /// </summary>
        private const float FOG_EDGE_MAX_SHARE = 0.25f;

        /// <summary>
        /// Доля глубины растушёвки, которая уходит на набор плотности: остаток у самого контура держится
        /// глухим. Глухая полоса прячет кромку мозаики — сквозь полупрозрачную пелену её прямая линия
        /// видна насквозь. Набор плотности при этом занимает большую часть глубины: короткий переход
        /// сам рисует прямой контур своей внутренней границей — тем заметнее, чем меньше глубина.
        /// </summary>
        private const float FOG_EDGE_SOLID_SHARE = 0.55f;

        /// <summary>Сторона квадратной текстуры градиента подложки. Переход плавный, детальности не требует.</summary>
        private const int FOG_TEXTURE_SIZE = 64;

        /// <summary>
        /// Предел стороны маски растушёвки в пикселях: у мира в тысячи клеток текстура «пиксель на клетку»
        /// стоила бы десятки мегабайт, а переход плавный и детальности не требует — лишние клетки
        /// укрупняются в один пиксель маски.
        /// </summary>
        private const int FOG_MASK_MAX_SIDE = 256;

        /// <summary>
        /// Размер метки в пикселях раскладки — из общего для обоих показов размера в клетках
        /// (<see cref="MinimapController.MARKER_TILES"/>), переведённого масштабом вписывания раскладки в
        /// окно (<see cref="WorldMapScale"/>). Своего числа обзорная карта не держит: величина точек
        /// задана одним местом на оба показа. Масштаб здесь на единичном увеличении — приближение растит
        /// метки вместе с картой (см. <see cref="ApplyWorldMapZoom"/>), потому размер и задан относительно
        /// раскладки.
        /// </summary>
        private static float MarkerPixels(float scale)
        {
            return MARKER_TILES * scale;
        }

        /// <summary>
        /// Цвет подложки области и растушёвки краёв мира — один на оба: карта уходит в фон САМОГО окна, и
        /// граница мозаики перестаёт читаться. Тёмный фон под светлой картой этого не давал — она смотрелась
        /// вырезанным прямоугольником, сколько бы ни растушёвывали её края.
        /// </summary>
        private static readonly Color BACKDROP_COLOR = new Color(0.42f, 0.44f, 0.47f);

        /// <summary>Идёт сборка раскладки: повторное открытие её не удваивает.</summary>
        private bool worldMapBuilding;

        /// <summary>
        /// Картинки карт, разложенные сейчас в окне. Держим их списком, потому что сборщик мусора
        /// нарисованные картинки (Texture2D, Sprite) не убирает — их снимает только Destroy, а раскладка
        /// собирается заново на КАЖДОМ открытии окна: без этого списка каждое открытие оставляло бы в
        /// памяти прежний комплект (на открытом мире это десяток мегабайт за раз, до конца игры).
        /// Радара это не касается: картинки тех же карт у него СВОИ (см. MinimapController), общий у них
        /// только PNG в кеше на диске.
        /// </summary>
        private readonly List<Sprite> worldMapImages = new List<Sprite>();

        /// <summary>
        /// Метки поверх раскладки — переходы и сущности. Держим списком, чтобы поднимать их над пеленой
        /// края мира; сами объекты уничтожаются вместе с прежней раскладкой при следующем открытии окна.
        /// </summary>
        private readonly List<RectTransform> worldMapMarkers = new List<RectTransform>();

        /// <summary>
        /// Переходы карты в клетках её собственной сетки, ключ — id карты. Разметку берём из локального
        /// кеша: на обзорной карте видны и те карты, которых сейчас в сцене нет вовсе, а разбор скачанной
        /// карты стоит дорого — держим разобранное до конца игры (карту перекачивают вместе со входом).
        /// </summary>
        private static readonly Dictionary<int, List<Vector2>> worldMapWarps = new Dictionary<int, List<Vector2>>();

        /// <summary>
        /// Легенда, собранная за сборку раскладки: подпись метки → цвета, которыми она на карте встретилась.
        /// Вид — не единственный ключ цвета: один и тот же вид контент игры вправе красить по-разному
        /// (сундук и портал — оба object), потому под подписью лежит НАБОР цветов, а не один. Подписи и
        /// цвета держим сортированными: сущности обходятся в порядке словаря карт, и без сортировки строки
        /// легенды переставлялись бы от открытия к открытию.
        /// </summary>
        private readonly SortedDictionary<string, SortedSet<string>> worldMapLegendKinds = new SortedDictionary<string, SortedSet<string>>();

        /// <summary>Пределы увеличения: единица — вся раскладка целиком в окне.</summary>
        private const float ZOOM_MIN = 1f;
        private const float ZOOM_MAX = 6f;

        /// <summary>Во сколько раз меняет увеличение одно нажатие кнопки.</summary>
        private const float ZOOM_STEP = 1.5f;

        /// <summary>Текущее увеличение раскладки. Переживает закрытие окна — игрок вернётся к тому же виду.</summary>
        private float worldMapZoom = ZOOM_MIN;

        protected override void Awake()
        {
            if (worldMapGroup == null)
            {
                Error("Карта мира: не присвоена группа окна worldMapGroup");
                return;
            }

            if (worldMapArea == null)
            {
                Error("Карта мира: не присвоена область раскладки worldMapArea");
                return;
            }

            if (worldMapTilePrefab == null)
            {
                Error("Карта мира: не присвоен префаб картинки карты worldMapTilePrefab");
                return;
            }

            if (worldMapContent == null)
            {
                Error("Карта мира: не присвоен контейнер раскладки worldMapContent");
                return;
            }

            if (worldMapPlayerMarker == null)
            {
                Error("Карта мира: не присвоена точка игрока worldMapPlayerMarker");
                return;
            }

            if (worldMapTitle == null)
            {
                Error("Карта мира: не присвоена подпись worldMapTitle");
                return;
            }

            if (worldMapLegend == null)
            {
                Error("Карта мира: не присвоена легенда worldMapLegend");
                return;
            }

            // Метка игрока — единственное, что игрок ищет на карте глазами, потому рисуем её сами: точка
            // без каймы теряется на пёстрой карте (песок, крыши, зелень), а кайма держит её видимой на любом.
            // Цвет её и размер ставятся там, где известны prefab игрока и масштаб раскладки
            // (см. PlaceWorldMapPlayer): масштаб зависит от охвата мира и до сборки раскладки неизвестен.

            // Разметка карт — снимок скачанного той игрой, в которую вошли: статика вход переживает.
            worldMapWarps.Clear();

            // Подложка области: в середине глухая тёмная, к своим краям сходит в прозрачность. Иначе тёмный
            // прямоугольник обрывался бы прямой линией на фоне окна — та же резкая граница, что и у карт.
            Image backdrop = worldMapArea.GetComponent<Image>();
            if (backdrop == null)
            {
                Error("Карта мира: у области раскладки нет подложки (Image)");
                return;
            }

            backdrop.sprite = BuildBackdropSprite();
            backdrop.color = Color.white;   // цвет несёт сам спрайт: у краёв он прозрачный

            base.Awake();
        }

        protected override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.N))
                OpenCloseWorldMap();

            if (worldMapGroup.alpha == 0)
                return;

            // Колесо мыши — то же увеличение, что кнопками: на телефоне их и хватает, а с клавиатурой
            // и мышью тянуться к кнопкам ради масштаба неудобно.
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
                SetWorldMapZoom(worldMapZoom * Mathf.Pow(ZOOM_STEP, wheel));

            // Точка игрока идёт за ним, пока окно открыто: игрок ходит и с раскрытой картой.
            if (!worldMapBuilding)
                PlaceWorldMapPlayer();
        }

        /// <summary>
        /// Открыть либо закрыть окно. Публичный — зовётся иконкой карты в панели (Inspector) и клавишей.
        /// Раскладка собирается на КАЖДОМ открытии: пока окно закрыто, игрок успевает пройти новые карты.
        /// </summary>
        public void OpenCloseWorldMap()
        {
            OpenClose(worldMapGroup);

            if (worldMapGroup.alpha > 0 && !worldMapBuilding)
                StartCoroutine(BuildWorldMap());
        }

        /// <summary>
        /// Раскладывает карты мира из кеша по их местам в открытом мире, вписывая охват в область окна.
        /// Картинку карты, которой ещё нет либо которая устарела, рисует по одной за кадр: сборка карты
        /// из тайлов стоит того же, что заход на неё в игре, и пачкой она подвесила бы кадр.
        /// </summary>
        private IEnumerator BuildWorldMap()
        {
            worldMapBuilding = true;
            worldMapTitle.text = ConnectController.world_name;

            // Точка игрока лежит в том же контейнере (двигается вместе с раскладкой при увеличении) — её
            // пересборка не трогает, остальное собирается заново.
            foreach (Transform old in worldMapContent)
                if (old != worldMapPlayerMarker.transform)
                    Destroy(old.gameObject);

            worldMapMarkers.Clear();
            worldMapLegendKinds.Clear();

            // Сами картинки прежней раскладки: уничтожение объекта, который их показывал, их не снимает.
            foreach (Sprite image in worldMapImages)
            {
                if (image == null)
                    continue;

                Destroy(image.texture);
                Destroy(image);
            }

            worldMapImages.Clear();

            Dictionary<int, TileCacheService.CachedMap> maps = TileCacheService.GetWorldMaps(GAME_ID, ConnectController.world);
            KeepOnlyCurrentInterior(maps, player == null ? -1 : player.map);

            // Карт нет — шапка текущей карты ещё не легла в кеш (RememberMap пишет её по завершении
            // скачивания JSON карты, а до этого момента её нет ни в одном мире). Показывать нечего, точку
            // игрока тоже: она без раскладки бессмысленна.
            if (maps.Count == 0)
            {
                worldMapPlayerMarker.gameObject.SetActive(false);
                ShowLegend();   // показывать нечего — и объяснять цвета нечего
                worldMapBuilding = false;
                yield break;
            }

            RectInt bounds = WorldBounds(maps);
            float scale = WorldMapScale(bounds);

            foreach (KeyValuePair<int, TileCacheService.CachedMap> pair in maps)
            {
                byte[] png = TileCacheService.GetWorldMapImage(GAME_ID, pair.Key);

                if (png == null)
                {
                    png = WorldMapRenderer.Render(GAME_ID, pair.Key);

                    // Карта числится в кеше, а её файла нет — кеш чистили мимо владельца. Пропускаем:
                    // карта перекачается, когда игрок на неё зайдёт.
                    if (png == null)
                        continue;

                    TileCacheService.SaveWorldMapImage(GAME_ID, pair.Key, png);
                    yield return null;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(png);
                // Без сглаживания: за краем карты в картинке прозрачность, и фильтрация подмешивает её в
                // крайние пиксели — на стыке двух карт это читается тёмной полосой. Графика и так пиксельная.
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                Sprite image = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                worldMapImages.Add(image);

                GameObject tile = Instantiate(worldMapTilePrefab, worldMapContent);
                tile.name = pair.Key.ToString();
                tile.GetComponent<Image>().sprite = image;

                RectTransform rect = tile.GetComponent<RectTransform>();
                Rect place = MapImageRect(pair.Value, bounds, scale);

                rect.anchoredPosition = place.position;
                rect.sizeDelta = place.size;

                // Переходы этой карты: точки ложатся сразу за её картинкой — карты в раскладке не
                // перекрываются, и следующая закрыть их собой не может.
                foreach (Vector2 warp in MapWarps(pair.Key))
                {
                    AddContentMarker(pair.Value, warp, MarkerSprite(WARP_COLOR), bounds, scale);
                    // Своего вида у разметки нет — в легенде переход зовётся тем же словом, которым его
                    // зовёт сама игра: класс объекта-перехода приходит с её настройками при входе.
                    RememberLegend(ConnectController.warp_class, WARP_COLOR);
                }
            }

            // Растушёвка края мира — одним слоем поверх всей мозаики: край считается по внешнему контуру
            // раскладки, а не по сторонам отдельных карт, потому уступ границы (сосед короче либо смещён)
            // пелена обходит сама. Полосами по сторонам она обрывалась на стыке с соседом прямой линией.
            Sprite fog = BuildFogSprite(bounds, maps);
            worldMapImages.Add(fog);

            GameObject fogTile = Instantiate(worldMapTilePrefab, worldMapContent);
            fogTile.name = "Fog";
            fogTile.GetComponent<Image>().sprite = fog;

            RectTransform fogRect = fogTile.GetComponent<RectTransform>();
            Vector2 fogCorner = AreaPosition(bounds.xMin, bounds.yMin, bounds, scale);
            Vector2 fogOpposite = AreaPosition(bounds.xMax, bounds.yMax, bounds, scale);

            fogRect.anchoredPosition = new Vector2(Mathf.Round(fogCorner.x), Mathf.Round(fogCorner.y));
            fogRect.sizeDelta = new Vector2(
                Mathf.Round(fogOpposite.x) - Mathf.Round(fogCorner.x),
                Mathf.Round(fogCorner.y)   - Mathf.Round(fogOpposite.y)
            );

            // Метки переходов созданы в цикле выше и оказались под пеленой — поднимаем: они показывают
            // устройство места, а не сам рисунок карты, и гасить их вместе с ним нечего.
            foreach (RectTransform marker in worldMapMarkers)
                marker.SetAsLastSibling();

            AddEntityMarkers(maps, bounds, scale);
            AddGateMarkers(maps, bounds, scale);

            // Своя метка — такая же строка легенды, как чужие: она на карте видна, и цвет ей задаёт тот же
            // контент игры. Условие показа спрашиваем то же, по которому её ставит PlaceWorldMapPlayer:
            // цвета у вида нет — метки нет, и строки о ней быть не должно.
            if (player != null)
            {
                Color? own = MarkerColor(player.prefab);

                if (own != null)
                    RememberLegend(AnimationCacheService.GetPrefabKind(player.prefab), own.Value);
            }

            ShowLegend();

            // Точку игрока — поверх карт: она лежит в том же контейнере, а карты добавляются в него после
            // неё и иначе закрывают её собой.
            worldMapPlayerMarker.transform.SetAsLastSibling();

            worldMapBuilding = false;
            ApplyWorldMapZoom();
            PlaceWorldMapPlayer();
        }

        /// <summary>
        /// Переходы карты — середины их площадей в координатах самой карты (та же система, что у сущностей
        /// внутри неё: ось Y смотрит вниз отрицательными значениями). Разметка берётся из локального кеша,
        /// а не со сцены: на обзорной карте видны и карты, которых в сцене сейчас нет — сервер отдаёт лишь
        /// свою и смежные. Карты нет в кеше — переходов нет.
        /// </summary>
        private static List<Vector2> MapWarps(int mapId)
        {
            if (worldMapWarps.TryGetValue(mapId, out List<Vector2> known))
                return known;

            List<Vector2> warps = new List<Vector2>();
            string json = TileCacheService.ReadCachedMap(GAME_ID, mapId);

            if (json != null)
            {
                Map map = MapDecodeModel.parse(json);

                // Что считать переходом, решает один отбор на весь клиент (WarpMarker): свечение на земле,
                // точка радара и точка здесь зажигаются от него же.
                foreach (LayerObject warp in WarpMarker.Objects(map))
                    warps.Add(WarpMarker.Center(warp, map.tilewidth, map.tileheight));
            }

            worldMapWarps[mapId] = warps;
            return warps;
        }

        /// <summary>
        /// Точки сущностей. Кого показывать, решает контент игры: точка есть у того, кому задан цвет метки
        /// (<see cref="MinimapController.MarkerColor"/>) и кто при этом НЕПОДВИЖЕН — обзорная карта про
        /// устройство мест, а живое на ней сменилось бы раньше, чем игрок дойдёт (радар показывает его сам).
        /// Сущности приходят с сервера только для карты игрока и смежных, потому и точки есть лишь у них;
        /// на прочих картах раскладки видна одна разметка.
        /// Их место в мире считается от карты игрока: сцена стоит её началом координат, а сама карта своё
        /// место в открытом мире знает (тот же счёт, что у точки игрока в <see cref="PlaceWorldMapPlayer"/>).
        /// </summary>
        private void AddEntityMarkers(Dictionary<int, TileCacheService.CachedMap> maps, RectInt bounds, float scale)
        {
            if (player == null || !maps.TryGetValue(player.map, out TileCacheService.CachedMap current))
                return;

            foreach (Transform mapZone in worldObject.transform)
            {
                foreach (Transform entityTransform in mapZone)
                {
                    EntityModel model = entityTransform.GetComponent<EntityModel>();

                    if (model == null || model == player)
                        continue;                               // игрок — своя метка, крупнее прочих
                    if (model.action == ACTION_REMOVE)
                        continue;                               // удаляемых с карты не рисуем

                    Color? color = MarkerColor(model.prefab);
                    if (color == null)
                        continue;   // цвета метки у вида нет — контент игры его на картах не показывает

                    // Ходячее обзорная карта не показывает: она про устройство мест, а кто где ходит
                    // СЕЙЧАС — дело радара, там окружение игрока и видно живьём. Подвижность спрашиваем у
                    // контента игры: компонент скорости положен видам, которые ходят, и его отсутствие и
                    // есть «стоит на месте» — своего признака клиент для этого не заводит.
                    if (AnimationCacheService.GetComponentValue(model.prefab, EnemyModel.COMPONENT_SPEED, null) != null)
                        continue;

                    AddContentMarker(current, entityTransform.position, MarkerSprite(color.Value), bounds, scale);
                    RememberLegend(AnimationCacheService.GetPrefabKind(model.prefab), color.Value);
                }
            }
        }

        /// <summary>
        /// Метки проходов в недоступные соседние карты — крестами на свободных участках общей границы
        /// (<see cref="MapController.getGates"/>), теми же, что и на радаре. Место их считается от карты
        /// игрока, как и у сущностей: проходы приходят в координатах сцены, а её началом стоит его карта.
        /// Игрока нет либо его карты в раскладке нет — рисовать не от чего.
        ///
        /// Метка на проход одна, в его середине (<see cref="MapController.Gate.center"/>): мир на этой карте
        /// сжат сильнее, чем на радаре, и по клетке метки в неё не разложить.
        /// </summary>
        private void AddGateMarkers(Dictionary<int, TileCacheService.CachedMap> maps, RectInt bounds, float scale)
        {
            if (player == null || !maps.TryGetValue(player.map, out TileCacheService.CachedMap current))
                return;

            foreach (Gate gate in getGates())
                AddContentMarker(current, gate.center, UnavailableSprite(), bounds, scale);
        }

        /// <summary>
        /// Место картинки карты в области окна: якорная точка (левый верхний угол) и размер — ровно то, что
        /// ложится в <see cref="RectTransform.anchoredPosition"/> и <see cref="RectTransform.sizeDelta"/>,
        /// а не геометрический прямоугольник с осью Y вверх.
        ///
        /// Углы округляются до целого пикселя: у соседних карт координаты дробные, и на стыке иначе остаётся
        /// субпиксельная щель — сквозь неё видна подложка, и бесшовный переход читается тёмным швом.
        /// Растягивать картинку с запасом нельзя: крайний столбец её пикселей размазался бы по добавке и дал
        /// полосу уже светлую.
        ///
        /// Потребителей у прямоугольника два, и оба обязаны брать ОДИН: этим местом ложится сама картинка, и
        /// от него же считается место точки внутри карты (<see cref="MarkerPosition"/>). Считай точка по
        /// неокруглённой раскладке — она разъезжается с рисунком ровно на ту долю клетки, которую внесло
        /// округление, а увеличение раскладки растит этот съезд вместе с собой.
        /// </summary>
        private Rect MapImageRect(TileCacheService.CachedMap map, RectInt bounds, float scale)
        {
            Vector2 corner = AreaPosition(map.x, map.y, bounds, scale);
            Vector2 opposite = AreaPosition(map.x + map.width, map.y + map.height, bounds, scale);

            float left = Mathf.Round(corner.x);
            float top  = Mathf.Round(corner.y);

            return new Rect(left, top, Mathf.Round(opposite.x) - left, top - Mathf.Round(opposite.y));
        }

        /// <summary>
        /// Место точки в области окна. Точка приходит в координатах СЦЕНЫ — тех, в которых стоят сущности,
        /// проходы и разметка переходов, — а <paramref name="map"/> есть карта, началом координат которой
        /// эта сцена стоит. Картинка карты же считает клетки от своего угла (ось Y вниз), потому перевод
        /// держит <see cref="MapController.MapImageCell"/>: без него точка уезжает от картинки на полклетки
        /// влево и вверх. Клетка отсчитывается по ФАКТИЧЕСКОМУ месту картинки (<see cref="MapImageRect"/>),
        /// а не по точной раскладке — иначе точка не совпадает с рисунком на округление его угла.
        ///
        /// Перевод один на ВСЕ точки обзорной карты — переходы, сущности, проходы и метку игрока: пропусти
        /// его один вызывающий, и разъедется с рисунком только его точка, а прочие останутся на месте.
        /// </summary>
        private Vector2 MarkerPosition(TileCacheService.CachedMap map, Vector2 scenePoint, RectInt bounds, float scale)
        {
            Vector2 cell = MapImageCell(scenePoint);
            Rect place = MapImageRect(map, bounds, scale);

            return new Vector2(
                place.x + cell.x / map.width  * place.width,
                place.y - cell.y / map.height * place.height
            );
        }

        /// <summary>
        /// Метка поверх раскладки — тем же рисунком, что и на радаре: метки одни на оба показа. Кладётся в
        /// контейнер раскладки, потому двигается, обрезается и растёт вместе с ней
        /// (см. <see cref="ApplyWorldMapZoom"/>). Место считает <see cref="MarkerPosition"/>.
        /// </summary>
        private void AddContentMarker(TileCacheService.CachedMap map, Vector2 scenePoint, Sprite sprite, RectInt bounds, float scale)
        {
            GameObject marker = Instantiate(worldMapTilePrefab, worldMapContent);
            marker.name = "Marker";

            Image image = marker.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;   // цвет несёт спрайт

            RectTransform rect = marker.GetComponent<RectTransform>();
            // Якорная точка — середина метки: считаем место самой точки, а не её левого верхнего угла.
            rect.pivot = new Vector2(0.5f, 0.5f);
            float size = MarkerPixels(scale);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = MarkerPosition(map, scenePoint, bounds, scale);

            worldMapMarkers.Add(rect);
        }

        /// <summary>
        /// Запоминает метку для легенды: её подпись и цвет. Зовётся там же, где метка ложится на карту, —
        /// оттого в легенде оказывается ровно показанное, и пустых строк в ней не бывает.
        /// </summary>
        private void RememberLegend(string label, Color color)
        {
            if (string.IsNullOrEmpty(label))
                return;   // игра переходами по разметке не пользуется — подписи для них нет

            if (!worldMapLegendKinds.TryGetValue(label, out SortedSet<string> colors))
                worldMapLegendKinds[label] = colors = new SortedSet<string>();

            colors.Add(ColorUtility.ToHtmlStringRGB(color));
        }

        /// <summary>
        /// Легенда под картой: точка своего цвета и подпись рядом. Без неё цвет метки не значит ничего —
        /// на карте у всех точек одна форма, и различает их игрок только по цвету.
        /// Подпись сущности — её вид (kind), как он назван в контенте игры; перехода — слово самой игры
        /// (<see cref="ConnectController.warp_class"/>): своего вида у разметки нет. Собственных названий
        /// клиент не придумывает — у другой игры и виды, и переходы зовутся иначе.
        /// </summary>
        private void ShowLegend()
        {
            StringBuilder legend = new StringBuilder();

            foreach (KeyValuePair<string, SortedSet<string>> kind in worldMapLegendKinds)
                foreach (string hex in kind.Value)
                {
                    if (legend.Length > 0)
                        legend.Append("    ");

                    legend.Append("<color=#").Append(hex).Append(">●</color> ").Append(kind.Key);
                }

            worldMapLegend.text = legend.ToString();
        }

        /// <summary>Приблизить карту. Публичный — зовётся кнопкой в окне (Inspector).</summary>
        public void ZoomInWorldMap()
        {
            SetWorldMapZoom(worldMapZoom * ZOOM_STEP);
        }

        /// <summary>Отдалить карту. Публичный — зовётся кнопкой в окне (Inspector).</summary>
        public void ZoomOutWorldMap()
        {
            SetWorldMapZoom(worldMapZoom / ZOOM_STEP);
        }

        private void SetWorldMapZoom(float zoom)
        {
            worldMapZoom = Mathf.Clamp(zoom, ZOOM_MIN, ZOOM_MAX);
            ApplyWorldMapZoom();
        }

        /// <summary>
        /// Применяет увеличение к контейнеру раскладки. Масштабируется КОНТЕЙНЕР, а не пересчитывается
        /// раскладка: карты уже разложены, и увеличение не должно стоить пересборки. Смещение от перетаскивания
        /// при этом подтягивается в допустимые пределы — иначе после отдаления раскладка осталась бы уехавшей
        /// за край и в окне была бы пустота.
        ///
        /// Точки лежат в том же контейнере и растут вместе с ним: метка — часть карты, а не наклейка поверх
        /// неё, и приближение не должно менять её размер ОТНОСИТЕЛЬНО места, которое она отмечает.
        /// </summary>
        private void ApplyWorldMapZoom()
        {
            worldMapContent.localScale = new Vector3(worldMapZoom, worldMapZoom, 1f);
            worldMapContent.anchoredPosition = ClampWorldMapShift(worldMapContent.anchoredPosition);
        }

        /// <summary>
        /// Сдвиг раскладки перетаскиванием. Публичный — зовётся <see cref="WorldMapDrag"/> с области показа.
        /// </summary>
        public void MoveWorldMap(Vector2 shift)
        {
            worldMapContent.anchoredPosition = ClampWorldMapShift(shift);
        }

        /// <summary>
        /// Держит сдвинутую раскладку в пределах области: увеличенная часть может уходить за край ровно
        /// настолько, насколько она больше окна. Меньше окна (единичное увеличение) — сдвиг запрещён совсем.
        /// </summary>
        private Vector2 ClampWorldMapShift(Vector2 shift)
        {
            float limitX = Mathf.Max(0f, (worldMapArea.rect.width  * worldMapZoom - worldMapArea.rect.width)  / 2f);
            float limitY = Mathf.Max(0f, (worldMapArea.rect.height * worldMapZoom - worldMapArea.rect.height) / 2f);

            return new Vector2(
                Mathf.Clamp(shift.x, -limitX, limitX),
                Mathf.Clamp(shift.y, -limitY, limitY)
            );
        }

        /// <summary>
        /// Масштаб вписывания раскладки в область — с полем по краям. Без поля крайняя карта упирается в
        /// самую границу области, где подложка уже растворилась, и её край торчит за фоном окна.
        /// </summary>
        private float WorldMapScale(RectInt bounds)
        {
            const float padding = 0.9f;

            return Mathf.Min(
                worldMapArea.rect.width  / bounds.width,
                worldMapArea.rect.height / bounds.height
            ) * padding;
        }

        /// <summary>
        /// Интерьер того же world, но не карта игрока сейчас, — из мозаики убирает: несколько интерьеров
        /// делят один world (см. CachedMap.hasOpenworldPosition), у их записей нет второй настоящей позиции —
        /// без фильтра они наложились бы друг на друга в раскладке либо подменяли бы друг друга по порядку
        /// обхода Dictionary. Карты, стоящие в открытом мире, фильтр не трогает.
        /// </summary>
        private static void KeepOnlyCurrentInterior(Dictionary<int, TileCacheService.CachedMap> maps, int currentMapId)
        {
            List<int> stale = null;
            foreach (KeyValuePair<int, TileCacheService.CachedMap> pair in maps)
                if (!pair.Value.hasOpenworldPosition && pair.Key != currentMapId)
                    (stale ??= new List<int>()).Add(pair.Key);

            if (stale != null)
                foreach (int mapId in stale)
                    maps.Remove(mapId);
        }

        /// <summary>Охват раскладки в тайлах: от левого-верхнего угла самой крайней карты до правого-нижнего.</summary>
        private static RectInt WorldBounds(Dictionary<int, TileCacheService.CachedMap> maps)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            foreach (TileCacheService.CachedMap map in maps.Values)
            {
                minX = Mathf.Min(minX, map.x);
                minY = Mathf.Min(minY, map.y);
                maxX = Mathf.Max(maxX, map.x + map.width);
                maxY = Mathf.Max(maxY, map.y + map.height);
            }

            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Точка раскладки (тайлы открытого мира, ось Y вниз) в координатах области окна. Якорь элементов —
        /// левый верхний угол области: раскладка мира считается оттуда же.
        /// </summary>
        private Vector2 AreaPosition(float worldX, float worldY, RectInt bounds, float scale)
        {
            // Мозаика уже центрирована в области: масштаб вписывает охват по меньшей стороне, по большей
            // остаётся поле — делим его пополам, иначе раскладка липнет к левому верхнему углу.
            float padX = (worldMapArea.rect.width - bounds.width * scale) / 2f;
            float padY = (worldMapArea.rect.height - bounds.height * scale) / 2f;

            return new Vector2(
                 padX + (worldX - bounds.xMin) * scale,
                -padY - (worldY - bounds.yMin) * scale
            );
        }

        /// <summary>
        /// Ставит точку игрока по его месту во всём мире: положение его карты в раскладке плюс его положение
        /// внутри карты. Игрока ещё нет либо его карта в раскладке не стоит — точку прячем.
        /// </summary>
        private void PlaceWorldMapPlayer()
        {
            Dictionary<int, TileCacheService.CachedMap> maps = TileCacheService.GetWorldMaps(GAME_ID, ConnectController.world);
            KeepOnlyCurrentInterior(maps, player == null ? -1 : player.map);

            if (player == null || !maps.TryGetValue(player.map, out TileCacheService.CachedMap current))
            {
                worldMapPlayerMarker.gameObject.SetActive(false);
                return;
            }

            RectInt bounds = WorldBounds(maps);
            float scale = WorldMapScale(bounds);

            if (!worldMapPlayerMarker.gameObject.activeSelf)
                worldMapPlayerMarker.gameObject.SetActive(true);

            // Цвет игроков задаёт контент игры — тот же, что несут прочие игроки; своего от них отличает
            // размер метки, а не оттенок (чужих на обзорной карте нет вовсе — они не стоят на месте).
            ApplyPlayerMarker(worldMapPlayerMarker, player.prefab);

            // Крупнее прочих меток тем же множителем, что и на радаре, — своё место видно сразу.
            float own = MarkerPixels(scale) * PLAYER_MARKER_SCALE;
            worldMapPlayerMarker.rectTransform.sizeDelta = new Vector2(own, own);

            worldMapPlayerMarker.rectTransform.anchoredPosition = MarkerPosition(
                current,
                player.transform.position,
                bounds,
                scale
            );
        }

        /// <summary>
        /// Маска растушёвки на весь охват мира: альфа каждой клетки — по расстоянию до ближайшей клетки БЕЗ
        /// карты, то есть до внешнего контура раскладки. У самого контура фон глухой, вглубь мира сходит в
        /// прозрачность; стык двух карт и уступ границы (сосед короче либо смещён) обходятся сами — там
        /// клетка от края далеко, гасить нечего.
        ///
        /// Расстояние считается двумя проходами по сетке (chamfer 3-4: 3 — шаг по стороне, 4 — по диагонали),
        /// целочисленным приближением евклидова: по одним лишь сторонам пелена расходилась бы от угла ромбом.
        /// За пределами сетки карт нет — соседи вне её дают ноль, и край охвата сам считается контуром.
        /// </summary>
        private static Sprite BuildFogSprite(RectInt bounds, Dictionary<int, TileCacheService.CachedMap> maps)
        {
            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(bounds.width, bounds.height) / (float)FOG_MASK_MAX_SIDE));
            int width  = Mathf.Max(1, Mathf.CeilToInt(bounds.width  / (float)step));
            int height = Mathf.Max(1, Mathf.CeilToInt(bounds.height / (float)step));

            // Заведомо больше любого расстояния внутри сетки: дальше диагонали через весь охват не уйти.
            int far = (width + height) * 4;
            int[] distance = new int[width * height];

            foreach (TileCacheService.CachedMap map in maps.Values)
            {
                int fromX = Mathf.Clamp(Mathf.RoundToInt((map.x - bounds.xMin) / (float)step), 0, width);
                int toX   = Mathf.Clamp(Mathf.RoundToInt((map.x + map.width - bounds.xMin) / (float)step), 0, width);
                int fromY = Mathf.Clamp(Mathf.RoundToInt((map.y - bounds.yMin) / (float)step), 0, height);
                int toY   = Mathf.Clamp(Mathf.RoundToInt((map.y + map.height - bounds.yMin) / (float)step), 0, height);

                for (int y = fromY; y < toY; y++)
                    for (int x = fromX; x < toX; x++)
                        distance[y * width + x] = far;
            }

            int At(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? 0 : distance[y * width + x];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;

                    if (distance[index] == 0)
                        continue;

                    distance[index] = Mathf.Min(distance[index], Mathf.Min(
                        Mathf.Min(At(x - 1, y) + 3, At(x, y - 1) + 3),
                        Mathf.Min(At(x - 1, y - 1) + 4, At(x + 1, y - 1) + 4)
                    ));
                }

            for (int y = height - 1; y >= 0; y--)
                for (int x = width - 1; x >= 0; x--)
                {
                    int index = y * width + x;

                    if (distance[index] == 0)
                        continue;

                    distance[index] = Mathf.Min(distance[index], Mathf.Min(
                        Mathf.Min(At(x + 1, y) + 3, At(x, y + 1) + 3),
                        Mathf.Min(At(x + 1, y + 1) + 4, At(x - 1, y + 1) + 4)
                    ));
                }

            // Глубина в тех же единицах, что и расстояние: клетки укрупнены шагом сетки, шаг по стороне стоит 3.
            float depth = Mathf.Max(1f, Mathf.Min(FOG_EDGE_TILES, Mathf.Min(bounds.width, bounds.height) * FOG_EDGE_MAX_SHARE) / step) * 3f;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Плотность набирается ДО края: доля глубины у контура уже глухая, иначе прямая граница
                    // карты просвечивала бы сквозь полупрозрачную пелену.
                    float share = Mathf.Clamp01((1f - distance[y * width + x] / depth) / FOG_EDGE_SOLID_SHARE);
                    // Плавно на обоих концах: резкий старт сам рисует прямую линию там, где пелена начинается.
                    float alpha = share * share * (3f - 2f * share);

                    // У Texture2D нулевая строка — НИЖНЯЯ, а раскладка мира считается сверху вниз.
                    pixels[(height - 1 - y) * width + x] = new Color(BACKDROP_COLOR.r, BACKDROP_COLOR.g, BACKDROP_COLOR.b, alpha);
                }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Подложка области: глухая внутри, к своим краям сходит в прозрачность. Растушёвка карт уходит в
        /// тёмное, и без такой подложки тёмный прямоугольник области сам обрывался бы прямой линией на фоне
        /// окна — та же резкая граница, от которой избавляются края карт.
        /// </summary>
        private static Sprite BuildBackdropSprite()
        {
            Texture2D texture = new Texture2D(FOG_TEXTURE_SIZE, FOG_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            // Доля стороны, на которой подложка гаснет к краю. Та же роль, что у растушёвки карт, но мерится
            // от размера самой области — она одна на любое увеличение раскладки.
            const float fade = 0.08f;

            for (int y = 0; y < FOG_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < FOG_TEXTURE_SIZE; x++)
                {
                    float edge = Mathf.Min(
                        Mathf.Min(x, FOG_TEXTURE_SIZE - 1 - x),
                        Mathf.Min(y, FOG_TEXTURE_SIZE - 1 - y)
                    ) / (float)(FOG_TEXTURE_SIZE - 1);

                    float alpha = Mathf.Clamp01(edge / fade);
                    texture.SetPixel(x, y, new Color(BACKDROP_COLOR.r, BACKDROP_COLOR.g, BACKDROP_COLOR.b, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, FOG_TEXTURE_SIZE, FOG_TEXTURE_SIZE), new Vector2(0.5f, 0.5f));
        }
    }
}
