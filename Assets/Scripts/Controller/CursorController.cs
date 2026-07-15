using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public abstract class CursorController : LootWindowController
    {
        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private float horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private float vertical;

        private Vector3 move_to = Vector3.zero;

        [Header("Для работы с курсором и движением")]

        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        protected VariableJoystick joystick;

        /// <summary>
        /// Объект с компонентом Image
        /// </summary>
        [SerializeField]
        private Image cursor;

        /// <summary>
        /// An offset to move the icon away from the mouse
        /// </summary>
        [SerializeField]
        private Vector3 cursor_offset;

        /// <summary>
        /// Кольцо-подсветка кликабельной сущности (труп-контейнер) под курсором. Один переиспользуемый
        /// world-объект на сцене (НЕ создаём per-frame): двигаем к наведённой сущности, прячем когда её нет.
        /// </summary>
        [SerializeField]
        private SpriteRenderer hoverHighlight;

        /// <summary>
        /// если не null - то объект который двигаем
        /// </summary>
        public static MoveableObject MyMoveable;

        /// <summary>
        /// Источник Moveable'а — выставляется EquipmentSlot.HandlePointerClick когда игрок берёт
        /// экипированный item в курсор. Используется в Item.Use чтобы drop поверх инвентарного слота
        /// трактовался как unequip (отправка ui/equip/index {slug: null}), а не как простое движение
        /// предмета по инвентарю.
        /// Сбрасывается одновременно с MyMoveable.
        /// </summary>
        public static EquipmentSlot SourceEquipmentSlot;
        protected override void Awake()
        {
            base.Awake();

            if (cursor == null)
            {
                Error("не присвоен GameObject курсора с image компонентом");
                return;
            }
              
            if (joystick == null)
            {
                Error("не указан джойстик");
                return;
            }

            if (hoverHighlight == null)
            {
                Error("не назначено кольцо-подсветка кликабельной сущности (hoverHighlight)");
                return;
            }
            hoverHighlight.gameObject.SetActive(false);
        }

        /// <summary>
        /// если мы стреляем и продолжаем идти заблокируем поворот (он без запроса к серверу делется) в сторону хотьбы (а то спиной стреляем)
        /// </summary>
        private DateTime block_forward = DateTime.Now;

        protected override void Update ()
        {
            base.Update();

            //Makes sure that the icon follows the hand
            cursor.transform.position = Input.mousePosition + cursor_offset;

            UpdateHoverHighlight(Input.mousePosition);

            if (MyMoveable!=null)
                cursor.raycastTarget = true;
            else
                cursor.raycastTarget = false;

            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                cursor.raycastTarget = false;
                GameObject gameObject = null;

                // RaycastAll, а не одиночный Raycast: когда игрок СТОИТ на подбираемом предмете, его
                // собственный коллайдер (тело отрисовано поверх) перекрывает предмет, и одиночный raycast
                // вернул бы самого игрока — клик «по себе» не доходил до предмета, move_to/walk/to не
                // отправлялись, и серверный подбор (walk/to → item/pickup при совпадении тайла) не запускался.
                // Перебираем все попадания в порядке возрастания дистанции и берём первую сущность, КРОМЕ
                // своего игрока: предмет под ногами оказывается следующим хитом и становится целью клика.
                // Для врагов/NPC порядок тот же, что давал одиночный raycast (ближайший хит) — поведение
                // по ним не меняется. GetComponentInParent (а не GetComponent) — чтобы клик по дочернему
                // коллайдеру сущности (например по кликабельной надписи EquipableGroundMarker над предметом
                // на земле) считался кликом по самой сущности-корню. Корневой collider тела находит себя же.
                RaycastHit2D[] hits = Physics2D.RaycastAll(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero, Mathf.Infinity);
                EntityModel hitEntity = null;
                foreach (RaycastHit2D h in hits)
                {
                    if (h.transform == null) continue;
                    EntityModel e = h.transform.GetComponentInParent<EntityModel>();
                    if (e == null) continue;
                    if (PlayerController.Player != null && e == PlayerController.Player) continue;   // клик «сквозь себя» к предмету под ногами
                    hitEntity = e;
                    break;
                }
                if (hitEntity != null)
                {
                    gameObject = hitEntity.gameObject;
                    player.Log("Кликнули на объект " + gameObject.name);
                }

                else if
                (
                    (EventSystem.current.IsPointerOverGameObject() || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)))
                )
                {
                    PointerEventData pointerData = new PointerEventData(EventSystem.current);
                    pointerData.position = Input.mousePosition;

                    List<RaycastResult> results = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointerData, results);

                    if (results.Count > 0)
                    {
                        gameObject = results[0].gameObject;
                        player.Log("Кликнули на UI " + gameObject.name);
                    }    
                }

                if (MyMoveable != null)
                {
                    var held = MyMoveable;

                    if (player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
                    {
                        held.Use((Camera.main.ScreenToWorldPoint(Input.mousePosition) - PlayerController.Player.transform.position).normalized, gameObject);
                    }

                    // если Use() установил новый moveable (chain-swap) — не сбрасывать
                    if (MyMoveable == held)
                    {
                        MyMoveable = null;
                        SourceEquipmentSlot = null;
                        EquipmentController.ClearHighlight();
                        cursor.color = new Color(0, 0, 0, 0);

                        bool droppedOnInventorySlot = gameObject != null && gameObject.GetComponentInParent<SlotScript>() != null;
                        if (!droppedOnInventorySlot)
                            CloseAllMenu();
                    }
                }
                else
                {
                    if(gameObject == null)
                    {
                        Target = null;
                        persist_target = false;
                        Debug.Log("Кликнули на " + Camera.main.ScreenToWorldPoint(Input.mousePosition));

                        // движение к указанной клетке
                        if (player != null)
                        {
                            move_to = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                            if (Vector3.Distance(player.position, move_to) < 1.15f)
                                move_to = Vector3.zero;
                        }
                    }
                    else
                    {
                        ObjectModel new_target = gameObject.GetComponent<ObjectModel>();
                        if (new_target != null)
                        {
                            // Труп (мёртвая сущность) — контейнер лута: open с ЛЮБОЙ дистанции одним кликом,
                            // сервер сам ведёт игрока к трупу и повторяет открытие до прибытия (как fight/melee
                            // подходит к цели). move_to НЕ ставим: параллельный walk/to from_client снял бы
                            // серверный лут-подход (ручное движение = отмена). Кликнутый труп выбираем тем же
                            // правилом, что hover-кольцо (CorpseAtScreen) — куда подсветили, туда и кликнули.
                            if (new_target.action == "dead")
                            {
                                if (player != null)
                                {
                                    ObjectModel corpse = CorpseAtScreen(Input.mousePosition);
                                    if (corpse == null)
                                        corpse = new_target;

                                    // фрейм цели показывает кликнутый труп (кого лутаем); persist не
                                    // держим — нападающий перебьёт труп-цель автоматически (CanBeTarget
                                    // пропускает смену цели, у которой hp == 0)
                                    Target = corpse;
                                    persist_target = false;

                                    LootWindowController.Open(corpse.key);
                                }
                                return;
                            }

                            // КАК ПОДБИРАЮТСЯ ПРЕДМЕТЫ (kind=item / экипируемые):
                            // Отдельной клиентской команды "подобрать" НЕТ. Подбор серверный — сервер кладёт
                            // предмет в инвентарь, когда игрок касается клетки предмета (item/pickup на стороне
                            // сервера). Задача клиента — лишь ДОВЕСТИ игрока до предмета. Поэтому клик по
                            // предмету (или по его кликабельной надписи EquipableGroundMarker, чей collider
                            // через GetComponentInParent резолвится в этот же ObjectModel) трактуем как
                            // "идти к нему": ставим move_to в позицию предмета — дальше FixedUpdate шлёт
                            // WalkResponse "to", игрок подходит, сервер подбирает. Цель атаки (Target) для
                            // предмета не выставляем — UI-рамка цели предназначена врагам (см. TargetController).
                            if (!string.IsNullOrEmpty(new_target.prefab)
                                && AnimationCacheService.IsGroundItem(new_target.prefab))
                            {
                                Target = null;
                                persist_target = false;
                                if (player != null)
                                    move_to = gameObject.transform.position;
                            }
                            else
                            {
                                // Враг/NPC: выбираем как цель (UI-рамка + цель для заклинаний/атак по Target).
                                Target = new_target;
                                persist_target = true;
                            }
                        }
                    }
                }
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (player != null && player.action != ACTION_REMOVE)
            {
                try
                {
                    vertical = Input.GetAxis("Vertical") != 0 ? Input.GetAxis("Vertical") : joystick.Vertical;
                    horizontal = Input.GetAxis("Horizontal") != 0 ? Input.GetAxis("Horizontal") : joystick.Horizontal;

                    // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                    // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                    if (
                        (
                            move_to != Vector3.zero
                                 ||
                            vertical != 0
                                ||
                            horizontal != 0
                        )
                    )
                    {
                        if (vertical != 0 || horizontal != 0)
                        {
                            // я подогнал магнитуду под размер круга джойстика (выйдя за него мы уже будем идти а не менять направления)
                            if (Math.Abs(horizontal) > 0.5 || Math.Abs(vertical) > 0.5)
                            {
                                // не путать импульс нажатия кнопки в определенном направлении с forward (направлением движения, т.е нормальизованным вектором)
                                Vector3 vector = new Vector3(horizontal, vertical, 0).normalized;

                                // значение forward не сменится (тк его меняет только сервер) но запустится анимация при которой графика персонажа повернется
                                if (DateTime.Compare(block_forward, DateTime.Now) < 1)
                                   player.Forward = vector;

                                WalkResponse response = new WalkResponse();

                                response.x = Math.Round(vector.x, position_precision);
                                response.y = Math.Round(vector.y, position_precision);
                                response.Send();
                            }
                        }
                        else
                        {
                            WalkResponse response = new WalkResponse();

                            response.action = "to";
                            response.x = Math.Round(move_to.x, position_precision);
                            response.y = Math.Round(move_to.y, position_precision);
                            response.z = player.transform.position.z;
                            response.Send();

                            move_to = Vector3.zero;
                        }

                        // если с сервера пришла анимация заблокируем повороты вокруг себя на какое то время (а то спиной стреляем идя и стреляя)
                        block_forward = DateTime.Now.AddSeconds((double)player.getEvent(WalkResponse.GROUP).timeout);
                    }
                }
                catch (Exception ex)
                {
                    Error("Ошибка управелния игроком: ", ex);
                }
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {

            return base.UpdateObject(map_id, key, recive);
        }

        // множитель кольца к рендер-границам трупа: 1.0 — ровно по спрайту (прозрачные поля текстуры
        // сами дают небольшой зазор). Держать МЕНЬШЕ зазора кликабельного коллайдера трупа
        // (ObjectModel.CORPSE_HIT_GAP) — кольцо лежит ВНУТРИ хит-области, клик по кольцу попадает по трупу.
        private const float HIGHLIGHT_GAP = 1.0f;

        /// <summary>
        /// Hover-фидбек: навёл курсор на кликабельную сущность (труп-контейнер) — кольцо ВОКРУГ её спрайта
        /// (сигнал «кликабельно» + отличие трупа от пустой земли под курсором). Курсор ушёл — скрыть.
        /// Один переиспользуемый объект (двигаем/масштабируем, не пересоздаём).
        /// </summary>
        private void UpdateHoverHighlight(Vector3 screenPos)
        {
            ObjectModel hovered = CorpseAtScreen(screenPos);
            if (hovered != null)
            {
                FitHighlightTo(hovered);
                if (!hoverHighlight.gameObject.activeSelf)
                    hoverHighlight.gameObject.SetActive(true);
            }
            else if (hoverHighlight.gameObject.activeSelf)
                hoverHighlight.gameObject.SetActive(false);
        }

        /// <summary>
        /// Центрирует и масштабирует кольцо ПОД конкретный труп: центр и РАЗМЕРЫ по суммарным рендер-границам
        /// его спрайтов (EntityModel.TryGetVisualBounds) — облегает тело по ОБЕИМ осям (эллипс под аспект
        /// спрайта), а не описанной окружностью по большей стороне: у вытянутого трупа окружность по большей
        /// стороне заметно шире тела (жалоба «круг велик»). Те же границы использует ObjectModel для подгонки
        /// кликабельного коллайдера трупа — кольцо и хит-область совпадают. Нет спрайтов (редко) — по позиции.
        /// </summary>
        private void FitHighlightTo(EntityModel e)
        {
            float nativeX = hoverHighlight.sprite != null ? hoverHighlight.sprite.bounds.size.x : 1f;
            float nativeY = hoverHighlight.sprite != null ? hoverHighlight.sprite.bounds.size.y : 1f;

            Vector3 center;
            float wx, wy;
            if (e.TryGetVisualBounds(out Bounds b))
            {
                center = b.center;
                wx = b.size.x * HIGHLIGHT_GAP;
                wy = b.size.y * HIGHLIGHT_GAP;
            }
            else
            {
                center = e.transform.position;
                wx = wy = HIGHLIGHT_GAP;   // fallback: ~1 клетка
            }

            hoverHighlight.transform.position = new Vector3(center.x, center.y, hoverHighlight.transform.position.z);
            float sx = nativeX > 0.0001f ? wx / nativeX : 1f;
            float sy = nativeY > 0.0001f ? wy / nativeY : 1f;
            hoverHighlight.transform.localScale = new Vector3(sx, sy, 1f);
        }

        /// <summary>
        /// Кликабельный труп-контейнер под курсором. Первая значимая (НЕ-игрок) сущность по лучу решает
        /// тип взаимодействия: живой враг / предмет сверху → null (не подсвечиваем). Если она труп —
        /// среди ВСЕХ мёртвых под курсором выбираем БЛИЖАЙШЕГО к точке курсора (центры визуальных
        /// границ): у сваленных в кучу тел коллайдеры перекрываются, а порядок RaycastAll при точечном
        /// луче недетерминирован — без выбора по дистанции кольцо «прыгало бы» на соседнее тело.
        /// Тем же методом труп выбирает и клик (Update) — подсветка и клик всегда об одном теле.
        /// </summary>
        private ObjectModel CorpseAtScreen(Vector3 screenPos)
        {
            if (Camera.main == null || player == null) return null;

            Vector3 world = Camera.main.ScreenToWorldPoint(screenPos);
            RaycastHit2D[] hits = Physics2D.RaycastAll(world, Vector2.zero, Mathf.Infinity);

            bool corpseZone = false;            // первая значимая сущность — труп?
            ObjectModel nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t == null) continue;
                EntityModel e = t.GetComponentInParent<EntityModel>();
                if (e == null) continue;
                if (PlayerController.Player != null && e == PlayerController.Player) continue;   // «сквозь себя»

                ObjectModel obj = e as ObjectModel;
                bool dead = obj != null && obj.action == "dead";

                if (!corpseZone)
                {
                    if (!dead)
                        return null;            // сверху живой/предмет — боевой/подборный клик в приоритете
                    corpseZone = true;
                }

                if (!dead) continue;

                Vector3 center = e.TryGetVisualBounds(out Bounds b) ? b.center : e.transform.position;
                float dx = center.x - world.x, dy = center.y - world.y;
                float sqr = dx * dx + dy * dy;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = obj;
                }
            }
            return nearest;
        }
        /// <summary>
        /// Метод вызываемый при перетаскивании
        /// </summary>
        /// <param name="moveable">The moveable to pick up</param>
        public static void TakeMoveable(MoveableObject moveable)
        {
            MyMoveable = moveable;
            // Sprite берём из image (корневой). Для visual-slot'а Icon-child имеет тот же sprite,
            // но если в каком-то префабе icon=null — image остаётся источником.
            MainController.Instance.cursor.sprite = moveable.Image.sprite;
            MainController.Instance.cursor.color = Color.white;
            MainController.Instance.cursor.preserveAspect = true;
            // Scale курсора = scale видимой иконки. По варианту 2 (TASK_ui_icon_size.md) icon.localScale=1,
            // т.е. курсор берёт размер из собственного rect (size в UI не влияет) — не «микроскопический»
            // у предметов с большим server size. Если Icon=null — scale от image (тоже 1).
            Image scaleSrc = moveable.Icon != null ? moveable.Icon : moveable.Image;
            MainController.Instance.cursor.transform.localScale = scaleSrc.transform.localScale;

            // Подсветить совместимые equipment-слоты. Для не-Item moveable'ов очищаем подсветку,
            // чтобы chain-swap с предмета на не-предмет (если когда-нибудь появится) не оставлял
            // старую подсветку висящей.
            // Только предмет СВОЕГО инвентаря (SlotNum > 0): контракт ui/equip требует
            // inventory_idx > 0 (equip хранит ссылку на слот инвентаря) — предмет контейнера
            // (SlotNum == 0) надеть напрямую нельзя, подсветка обещала бы невозможное.
            EquipmentController.HighlightForItem(moveable is Item item && item.SlotNum > 0 ? item : null);
        }
    }
}
