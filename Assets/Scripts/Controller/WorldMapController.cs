using System.Collections;
using System.Collections.Generic;
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
    /// Отличие от мини-карты (<see cref="MinimapController"/>): та показывает живое окружение игрока камерой
    /// над реально выложенными тайлами — только текущую карту и смежные. Здесь мир целиком, включая места,
    /// откуда игрок давно ушёл, и рисуется он картинками, а не сценой.
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
        /// Ширина растушёвки края карты в клетках. Гасится ТОЛЬКО та сторона, у которой в раскладке нет
        /// соседа: там мир обрывается на границе исследованного, и без растушёвки обрыв режет глаз. Сторона
        /// со стыком остаётся чистой — иначе на месте бесшовного перехода появился бы тёмный шов.
        /// </summary>
        private const int FOG_EDGE_TILES = 25;

        /// <summary>Сторона квадратной текстуры градиента. Переход плавный, детальности не требует.</summary>
        private const int FOG_TEXTURE_SIZE = 64;

        /// <summary>Размер метки игрока на карте в пикселях панели. Увеличение её не растит (см. ApplyWorldMapZoom).</summary>
        private const float MARKER_SIZE = 22f;

        /// <summary>
        /// Цвет подложки области и растушёвки краёв карт — один на оба: карта уходит в фон САМОГО окна, и
        /// граница мозаики перестаёт читаться. Тёмный фон под светлой картой этого не давал — она смотрелась
        /// вырезанным прямоугольником, сколько бы ни растушёвывали её края.
        /// Берётся из спрайта окна при первом показе (см. ReadWindowColor), значение здесь — запасное.
        /// </summary>
        private static Color BACKDROP_COLOR = new Color(0.42f, 0.44f, 0.47f);

        /// <summary>Общий спрайт растушёвки: слева прозрачный, справа глухой. Поворотом кладётся на любую сторону.</summary>
        private Sprite worldMapEdgeSprite;

        /// <summary>Идёт сборка раскладки: повторное открытие её не удваивает.</summary>
        private bool worldMapBuilding;

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

            worldMapEdgeSprite = BuildEdgeSprite();

            // Метка игрока — единственное, что игрок ищет на карте глазами, потому рисуем её сами: точка
            // без каймы теряется на пёстрой карте (песок, крыши, зелень), а кайма держит её видимой на любом.
            worldMapPlayerMarker.sprite = BuildPlayerMarkerSprite();
            worldMapPlayerMarker.color = Color.white;   // цвет несёт спрайт
            worldMapPlayerMarker.rectTransform.sizeDelta = new Vector2(MARKER_SIZE, MARKER_SIZE);

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

            Dictionary<int, TileCacheService.CachedMap> maps = TileCacheService.GetWorldMaps(GAME_ID, ConnectController.world);

            // Карт нет — игрок в мире, ни одна карта которого в раскладке не стоит (интерьер, подземелье).
            // Показывать нечего, точку игрока тоже: она без раскладки бессмысленна.
            if (maps.Count == 0)
            {
                worldMapPlayerMarker.gameObject.SetActive(false);
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

                GameObject tile = Instantiate(worldMapTilePrefab, worldMapContent);
                tile.name = pair.Key.ToString();
                tile.GetComponent<Image>().sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);

                RectTransform rect = tile.GetComponent<RectTransform>();
                // Углы и размеры округляем до целого пикселя: у соседних карт координаты дробные, и на стыке
                // иначе остаётся субпиксельная щель — сквозь неё видна подложка, и бесшовный переход читается
                // тёмным швом. Растягивать картинку с запасом нельзя: крайний столбец её пикселей размазался
                // бы по добавке и дал полосу уже светлую.
                Vector2 corner = AreaPosition(pair.Value.x, pair.Value.y, bounds, scale);
                Vector2 opposite = AreaPosition(pair.Value.x + pair.Value.width, pair.Value.y + pair.Value.height, bounds, scale);

                rect.anchoredPosition = new Vector2(Mathf.Round(corner.x), Mathf.Round(corner.y));
                rect.sizeDelta = new Vector2(
                    Mathf.Round(opposite.x) - Mathf.Round(corner.x),
                    Mathf.Round(corner.y)   - Mathf.Round(opposite.y)
                );

                AddOpenEdges(rect, pair.Key, maps, scale);
            }

            // Точку игрока — поверх карт: она лежит в том же контейнере, а карты добавляются в него после
            // неё и иначе закрывают её собой.
            worldMapPlayerMarker.transform.SetAsLastSibling();

            worldMapBuilding = false;
            ApplyWorldMapZoom();
            PlaceWorldMapPlayer();
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
        /// </summary>
        private void ApplyWorldMapZoom()
        {
            worldMapContent.localScale = new Vector3(worldMapZoom, worldMapZoom, 1f);
            worldMapContent.anchoredPosition = ClampWorldMapShift(worldMapContent.anchoredPosition);

            // Точку игрока увеличение не растит: она метка, а не часть карты — на шестикратном приближении
            // раздулась бы в пятно на пол-области. Место она держит, размер остаётся прежним.
            worldMapPlayerMarker.transform.localScale = Vector3.one / worldMapZoom;
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

            if (player == null || !maps.TryGetValue(player.map, out TileCacheService.CachedMap current))
            {
                worldMapPlayerMarker.gameObject.SetActive(false);
                return;
            }

            RectInt bounds = WorldBounds(maps);
            float scale = WorldMapScale(bounds);

            // Внутри карты игрок стоит в координатах Unity (Y вверх, вниз от верха карты — отрицательный),
            // а раскладка мира считает Y вниз — потому вертикаль берётся со знаком минус.
            Vector3 position = player.transform.position;

            if (!worldMapPlayerMarker.gameObject.activeSelf)
                worldMapPlayerMarker.gameObject.SetActive(true);

            worldMapPlayerMarker.rectTransform.anchoredPosition = AreaPosition(
                current.x + position.x,
                current.y - position.y,
                bounds,
                scale
            );
        }

        /// <summary>
        /// Вешает растушёвку на те стороны карты, где раскладка обрывается — соседней карты в кеше нет.
        /// Полоска кладётся ВНУТРЬ картинки карты, поворотом общего градиента: глухой край наружу, прозрачный
        /// внутрь. Стык двух карт остаётся чистым — там мир продолжается, гасить нечего.
        /// </summary>
        private void AddOpenEdges(RectTransform tile, int mapId, Dictionary<int, TileCacheService.CachedMap> maps, float scale)
        {
            TileCacheService.CachedMap map = maps[mapId];
            float depth = FOG_EDGE_TILES * scale;
            float width = map.width * scale;
            float height = map.height * scale;

            // Полоска кладётся центром и поворачивается: её собственный градиент идёт сверху вниз (глухой →
            // прозрачный), поворот разворачивает глухой конец наружу карты.
            if (!HasNeighbour(map, maps, mapId, 0, -1))
                AddEdge(tile, new Vector2(width, depth), new Vector2(width / 2f, -depth / 2f), 0f);

            if (!HasNeighbour(map, maps, mapId, 0, 1))
                AddEdge(tile, new Vector2(width, depth), new Vector2(width / 2f, -(height - depth / 2f)), 180f);

            if (!HasNeighbour(map, maps, mapId, -1, 0))
                AddEdge(tile, new Vector2(height, depth), new Vector2(depth / 2f, -height / 2f), 90f);

            if (!HasNeighbour(map, maps, mapId, 1, 0))
                AddEdge(tile, new Vector2(height, depth), new Vector2(width - depth / 2f, -height / 2f), -90f);
        }

        /// <summary>
        /// Есть ли у карты сосед с указанной стороны (dx/dy — направление в раскладке, y вниз). Соседство —
        /// касание границ с перекрытием по другой оси: так же, как его считает сервер, раздавая смежные карты.
        /// </summary>
        private static bool HasNeighbour(TileCacheService.CachedMap map, Dictionary<int, TileCacheService.CachedMap> maps, int mapId, int dx, int dy)
        {
            foreach (KeyValuePair<int, TileCacheService.CachedMap> pair in maps)
            {
                if (pair.Key == mapId)
                    continue;

                TileCacheService.CachedMap other = pair.Value;

                if (dx != 0)
                {
                    bool touches = dx < 0 ? other.x + other.width == map.x : other.x == map.x + map.width;
                    if (touches && other.y < map.y + map.height && map.y < other.y + other.height)
                        return true;
                }
                else
                {
                    bool touches = dy < 0 ? other.y + other.height == map.y : other.y == map.y + map.height;
                    if (touches && other.x < map.x + map.width && map.x < other.x + other.width)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Одна полоска растушёвки внутри картинки карты: размер (длина стороны × глубина), центр полоски
        /// от левого верха карты, поворот вокруг центра.
        /// </summary>
        private void AddEdge(RectTransform tile, Vector2 size, Vector2 center, float angle)
        {
            GameObject edge = Instantiate(worldMapTilePrefab, tile);
            edge.name = "Edge";

            Image image = edge.GetComponent<Image>();
            image.sprite = worldMapEdgeSprite;
            image.type = Image.Type.Simple;

            RectTransform rect = edge.GetComponent<RectTransform>();
            // Поворот идёт вокруг pivot — берём центр полоски, тогда положение считается одинаково для всех
            // четырёх сторон, а угол лишь разворачивает градиент.
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = center;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Общий спрайт растушёвки: по вертикали от глухого чёрного (верх) к полностью прозрачному (низ).
        /// Рисуется кодом — градиент без ассета; на каждую сторону кладётся поворотом.
        /// </summary>
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

        private static Sprite BuildEdgeSprite()
        {
            // Текстура КВАДРАТНАЯ, хотя градиент нужен только по вертикали: полоска шириной в один пиксель
            // при растяжении рисуется пустой.
            Texture2D texture = new Texture2D(FOG_TEXTURE_SIZE, FOG_TEXTURE_SIZE, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < FOG_TEXTURE_SIZE; y++)
            {
                // Верх текстуры — глухой край (он смотрит наружу карты), низ — прозрачный (внутрь карты).
                // У Texture2D нулевая строка — НИЖНЯЯ, потому доля растёт вместе с y.
                float alpha = y / (float)(FOG_TEXTURE_SIZE - 1);
                // Тот же цвет, что у подложки: край карты растворяется в фоне окна, а не в чужой черноте.
                Color color = new Color(BACKDROP_COLOR.r, BACKDROP_COLOR.g, BACKDROP_COLOR.b, alpha);

                for (int x = 0; x < FOG_TEXTURE_SIZE; x++)
                    texture.SetPixel(x, y, color);
            }

            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, FOG_TEXTURE_SIZE, FOG_TEXTURE_SIZE), new Vector2(0.5f, 0.5f));
        }
    }
}
