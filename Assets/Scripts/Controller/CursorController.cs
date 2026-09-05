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

        /// <summary>
        /// Отклонение джойстика, за которым начинается ходьба: подогнано под размер его круга — пока палец
        /// внутри, он выбирает направление, выйдя за круг — уже ведёт персонажа.
        /// </summary>
        private const float WALK_THRESHOLD = 0.5f;

        /// <summary>
        /// На джойстик опустили палец, а разобрано это касание ещё не было. Ставит реле нажатия, снимает
        /// <see cref="HandleMovementInput"/> — там уже прочитано отклонение джойстика: порядок обхода
        /// обработчиков объекта и порядок Update'ов сцены нам не подчинены, и решение, принятое в самом
        /// реле, зависело бы от того, успел ли джойстик обновить своё значение.
        /// </summary>
        private bool joystick_touched;

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

            // Касание джойстика игра слышит сама: своего сигнала о нём чужой пакет джойстика не даёт, а
            // разбор управления читает лишь отклонение — нажатие в центр от нетронутого джойстика по нему
            // неотличимо. Реле вешаем кодом, как ответ клавишей Enter у окна количества: объект джойстика
            // лежит в сцене, а связка живёт ровно столько, сколько сам контроллер.
            PointerDownRelay relay = joystick.GetComponent<PointerDownRelay>();
            if (relay == null)
                relay = joystick.gameObject.AddComponent<PointerDownRelay>();
            relay.Pressed = () => joystick_touched = true;

            // До прихода настроек джойстик скрыт: показывает его настройка игрока, а она приезжает уже
            // после входа — иначе он мелькал бы у того, кто им не пользуется. Не объявлена игрой вовсе —
            // остаётся скрытым (SettingsController, ветка рядом с «Тестовым режимом»).
            joystick.gameObject.SetActive(false);

            // Вход в игру начинается с пустых рук, а состояние «в руке» этого не гарантирует ни с одной
            // стороны: картинка курсора хранит в сцене тот цвет, на котором её оставили в редакторе (без
            // спрайта Image рисует прямоугольник самим цветом — по экрану за мышью ездит блёклое пятно),
            // а MyMoveable и SourceEquipmentSlot статические и переживают выход из игры (см. skill csharp,
            // «Сборки и статика»), так что повторный вход поднимал бы предмет из прошлой сессии.
            ReleaseCursor();
        }

        /// <summary>
        /// если мы стреляем и продолжаем идти заблокируем поворот (он без запроса к серверу делется) в сторону хотьбы (а то спиной стреляем)
        /// </summary>
        private DateTime block_forward = DateTime.Now;

        protected override void Update ()
        {
            base.Update();

            HandleMovementInput();

            //Makes sure that the icon follows the hand
            cursor.transform.position = InputSource.MousePosition + cursor_offset;

#if UNITY_EDITOR
            // Съёмка ролика ведёт указатель сценарием, и рисовать его в кадре приходится этой же рукой:
            // системный курсор рисует система поверх картинки окна, в запись он не попадает. Сценарий не
            // идёт — вызов не делает ничего.
            InputSource.DrawPointer(cursor);
#endif

            UpdateHoverHighlight(InputSource.MousePosition);
            UpdateCursorShape();

            // Удерживаемый предмет мог быть уничтожен, пока висел на курсоре: пересборка слотов окна
            // добычи (пришла дельта состава) уничтожает Item'ы вместе со слотами. Unity-сравнение с null
            // для уничтоженного объекта истинно, поэтому блок освобождения ниже — он стоит под
            // «MyMoveable != null» — такой предмет не ловит вовсе. Курсор обязан освобождаться ВСЕГДА,
            // поэтому потерю предмета снимаем здесь, до разбора клика.
            if (!ReferenceEquals(MyMoveable, null) && MyMoveable == null)
                ReleaseCursor();

            if (MyMoveable!=null)
                cursor.raycastTarget = true;
            else
                cursor.raycastTarget = false;

            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (InputSource.MouseDown)
            {
                cursor.raycastTarget = false;
                GameObject gameObject = null;

                // Нажатие, попавшее в элемент интерфейса, до мира не доходит вовсе: интерфейс разбираем
                // ПЕРВЫМ, мировой луч пускаем только на пустом под указателем интерфейсе. Иначе касание
                // круга джойстика — им игрок просит персонажа ВСТАТЬ — над сущностью выбирало бы её целью,
                // а у сущности без добычи ещё и ставило маршрут к ней: остановка снималась бы тем же
                // касанием, которым заказана.
                GameObject uiHit = ScreenUiAtPointer(InputSource.MousePosition);
                bool overUi = uiHit != null;
                if (overUi)
                {
                    gameObject = uiHit;
                    player.Log("Кликнули на UI " + gameObject.name);
                }
                else
                {
                    // RaycastAll, а не одиночный Raycast: когда игрок СТОИТ на подбираемом предмете, его
                    // собственный коллайдер (тело отрисовано поверх) перекрывает предмет, и одиночный raycast
                    // вернул бы самого игрока — клик «по себе» не доходил до предмета, и команда подбора
                    // (item/pickup) не отправлялась вовсе: вещь под ногами поднять было нечем.
                    // Перебираем все попадания в порядке возрастания дистанции и берём первую сущность, КРОМЕ
                    // своего игрока: предмет под ногами оказывается следующим хитом и становится целью клика.
                    // Для врагов/NPC порядок тот же, что давал одиночный raycast (ближайший хит) — поведение
                    // по ним не меняется. GetComponentInParent (а не GetComponent) — чтобы клик по дочернему
                    // коллайдеру сущности (например по кликабельной надписи EquipableGroundMarker над предметом
                    // на земле) считался кликом по самой сущности-корню. Корневой collider тела находит себя же.
                    RaycastHit2D[] hits = Physics2D.RaycastAll(Camera.main.ScreenToWorldPoint(InputSource.MousePosition), Vector2.zero, Mathf.Infinity);
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
                }

                if (MyMoveable != null)
                {
                    var held = MyMoveable;

                    if (player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
                    {
                        held.Use((Camera.main.ScreenToWorldPoint(InputSource.MousePosition) - PlayerController.Player.transform.position).normalized, gameObject);
                    }

                    // если Use() установил новый moveable (chain-swap) — не сбрасывать
                    if (MyMoveable == held)
                    {
                        ReleaseCursor();

                        bool droppedOnInventorySlot = gameObject != null && gameObject.GetComponentInParent<SlotScript>() != null;
                        if (!droppedOnInventorySlot)
                            CloseAllMenu();
                    }
                }
                // Пустые руки: нажатие трактуем по миру, только когда его не взял интерфейс. Иначе нажатие
                // по элементу, под которым мира не видно, читалось бы кликом по земле — персонаж уходил бы
                // под нажатую кнопку.
                else if (!overUi)
                {
                    if(gameObject == null)
                    {
                        Target = null;
                        persist_target = false;
                        LootWindowController.CancelPending();   // пошли в другое место — отложенное открытие добычи снимается
                        Debug.Log("Кликнули на " + Camera.main.ScreenToWorldPoint(InputSource.MousePosition));

                        // движение к указанной клетке
                        WalkToCursor();
                    }
                    else
                    {
                        ObjectModel new_target = gameObject.GetComponent<ObjectModel>();
                        if (new_target != null)
                        {
                            // Контейнер добычи — труп существа либо объект-сундук. Состав добычи приватен,
                            // поэтому клик шлёт команду открытия; подход к клетке контейнера ведёт сам
                            // сервер, движение клиентом не дублируем. Кликнутый контейнер выбираем тем же
                            // правилом, что hover-кольцо (ContainerAtScreen) — куда подсветили, туда и
                            // кликнули; кучей лежат только тела, у одиночного сундука выбор тривиален.
                            ObjectModel container = ContainerAtScreen(InputSource.MousePosition);
                            if (container == null && LootWindowController.IsContainer(new_target))
                                container = new_target;

                            if (container != null)
                            {
                                if (player != null)
                                {
                                    // Рамка цели показывает кликнутый контейнер — и труп, и сундук с лавкой:
                                    // по выбранному открывается окно сведений, а там о торговце и сундуке
                                    // есть что рассказать. Полоски состояния у них погаснут сами — значений,
                                    // которых у вида нет, сервер не пришлёт. persist не держим: нападающий
                                    // перебьёт такую цель автоматически (CanBeTarget пропускает смену цели,
                                    // у которой hp == 0).
                                    Target = container;
                                    persist_target = false;

                                    LootWindowController.RequestOpen(container);
                                }
                                return;
                            }

                            // КАК ПОДБИРАЮТСЯ ПРЕДМЕТЫ (kind=item / экипируемые):
                            // Клик по вещи (или по её кликабельной надписи EquipableGroundMarker, чей collider
                            // через GetComponentInParent резолвится в этот же ObjectModel) шлёт серверу команду
                            // подбора с ключом вещи. Персонажа к ней клиент НЕ ведёт: вещь дальше дальности —
                            // сервер сам подводит к ней и повторяет подбор до прибытия, а своя команда движения
                            // игрока этот подход отменяет, так что движение, поставленное тем же кликом, сняло бы
                            // подбор, который сам же и заказало. Тем же каналом и по той же причине уходит
                            // открытие контейнера (ветка выше). Целью вещь при этом выбираем: по выбранному
                            // открывается окно сведений, и о лежащей вещи там есть что рассказать — что это,
                            // чего стоит.
                            // Мёртвым не шлём вовсе: подбор сервер исполняет только живому (тот же гейт, что у
                            // применения предмета из курсора выше).
                            if (!string.IsNullOrEmpty(new_target.prefab)
                                && AnimationCacheService.IsGroundItem(new_target.prefab))
                            {
                                Target = new_target;
                                persist_target = false;
                                LootWindowController.CancelPending();

                                if (PlayerController.Player != null && PlayerController.Player.hp > 0)
                                {
                                    PickupResponse response = new PickupResponse();
                                    response.target = new_target.key;
                                    response.Send();
                                }
                            }
                            // Живой враг/NPC: выбираем как цель (UI-рамка + цель для заклинаний/атак по Target).
                            else if (new_target is EnemyModel && new_target.action != "dead")
                            {
                                Target = new_target;
                                persist_target = true;
                                LootWindowController.CancelPending();
                            }
                            // Сущность без добычи — объект без неё (портал, алтарь) либо труп существа,
                            // с которого нечего взять: взаимодействия нет, поэтому клик заодно трактуем
                            // как клик по земле — иначе кликабельная зона объекта работала бы «мёртвой»
                            // клеткой, на которую персонажа не отправить. Целью выбираем и её: рассказать
                            // о выбранном окно сведений может о ком угодно.
                            else
                            {
                                Target = new_target;
                                persist_target = false;
                                LootWindowController.CancelPending();
                                WalkToCursor();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Переиспользуемые буферы разбора наведения: он идёт каждый кадр, а новый список и событие на
        /// кадр — мусор на ровном месте.
        /// </summary>
        private readonly List<RaycastResult> hoverHits = new List<RaycastResult>();

        private PointerEventData hoverPointer;

        /// <summary>Кадр и точка экрана, которыми заполнен <see cref="hoverHits"/>.</summary>
        private int hoverHitsFrame = -1;

        private Vector3 hoverHitsAt;

        /// <summary>
        /// Попадания указателя в холсты сцены. Считаем лучом EventSystem: IsPointerOverGameObject отвечает
        /// про СИСТЕМНУЮ мышь, а при съёмке ролика указатель ведёт сценарий и системная мышь стоит не там
        /// (см. <see cref="InputSource"/> — указатель читается одной точкой). Касание пальцем покрыто тем же
        /// лучом: позиция касания приходит в Input.mousePosition, откуда её и берёт InputSource.
        /// Результат лежит в общем буфере и живёт до следующего вызова.
        ///
        /// За кадр по одной точке луч идёт ОДИН раз: спрашивают его и подсветка контейнера, и форма
        /// указателя, и разбор нажатия, а обходит он ВСЕ холсты сцены — экранные вместе с мировыми над
        /// каждым существом.
        /// </summary>
        private List<RaycastResult> RaycastUi(Vector3 screenPos)
        {
            if (hoverHitsFrame == Time.frameCount && hoverHitsAt == screenPos)
                return hoverHits;

            hoverHitsFrame = Time.frameCount;
            hoverHitsAt = screenPos;
            hoverHits.Clear();

            if (EventSystem.current == null)
                return hoverHits;

            if (hoverPointer == null)
                hoverPointer = new PointerEventData(EventSystem.current);

            hoverPointer.position = screenPos;
            EventSystem.current.RaycastAll(hoverPointer, hoverHits);

            return hoverHits;
        }

        /// <summary>
        /// Элемент ЭКРАННОГО холста под указателем либо null, когда под указателем интерфейса нет. Экранный
        /// холст лежит поверх мировых, и то, что в него попало, мира под собой не отдаёт: нажатие туда не
        /// доходит, кольцо наведения там не рисуется. Мировые холсты тем же лучом приходят наравне — имя и
        /// полоски над существом, надпись над лежащей вещью, — но они часть МИРА и разбираются мировым
        /// лучом: кликабельная надпись у вещи ради этого и заведена.
        /// </summary>
        private GameObject ScreenUiAtPointer(Vector3 screenPos)
        {
            List<RaycastResult> hits = RaycastUi(screenPos);

            for (int i = 0; i < hits.Count; i++)
            {
                // Рука курсора интерфейсом под указателем не считается: пока предмет несут, её
                // raycastTarget включён, и она перекрывала бы собой всё, на что указывают.
                if (cursor != null && hits[i].gameObject == cursor.gameObject)
                    continue;

                Canvas canvas = hits[i].gameObject.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                    continue;

                return hits[i].gameObject;
            }

            return null;
        }

        /// <summary>
        /// Форма указателя на всю игру решается здесь одной точкой: элементы интерфейса лишь отвечают,
        /// возьмёт ли их нажатие (<see cref="ITakeable"/>), и своей формы не ставят — иначе одно окно
        /// ставило бы ладонь, а соседнее её тут же снимало.
        ///
        /// Пока предмет несут, указатель обычный: его картинку курсор уже рисует сам, и ладонь поверх
        /// неё только мешала бы разглядеть, что именно в руке.
        /// </summary>
        private void UpdateCursorShape()
        {
            // Тянут карту мира — форму держит она сама: указатель при тяге уходит за края области.
            if (WorldMapDrag.Dragging)
                return;

            if (MyMoveable != null || EventSystem.current == null)
            {
                HandCursor.Set(HandCursor.Shape.Default);
                return;
            }

            List<RaycastResult> hits = RaycastUi(InputSource.MousePosition);

            HandCursor.Shape shape = HandCursor.Shape.Default;

            for (int i = 0; i < hits.Count; i++)
            {
                // Признак ищем ВВЕРХ по иерархии: попадание приходит в надпись либо рамку внутри
                // карточки, а отвечает за взятие сама карточка либо ячейка.
                ITakeable takeable = hits[i].gameObject.GetComponentInParent<ITakeable>();

                if (takeable == null)
                    continue;

                // Верхний элемент решает за всех: он и перехватит нажатие, а лежащее под ним недоступно.
                shape = takeable.CanTake ? HandCursor.Shape.Open : HandCursor.Shape.Default;
                break;
            }

            HandCursor.Set(shape);
        }

        /// <summary>
        /// Отправить персонажа в точку под курсором. Клик в упор (ближе шага) движения не даёт — иначе
        /// сервер получал бы команду на клетку, где игрок уже стоит. Заведомо холостой клик не шлётся вовсе
        /// (см. <see cref="CanServerWalkTo"/>).
        /// </summary>
        private void WalkToCursor()
        {
            if (player == null) return;

            move_to = Camera.main.ScreenToWorldPoint(InputSource.MousePosition);
            if (Vector3.Distance(player.position, move_to) < 1.15f || !CanServerWalkTo(move_to))
                move_to = Vector3.zero;
        }

        /// <summary>
        /// Найдёт ли сервер по этому клику хоть какую-то клетку, куда вести. Зеркало серверной ветки движения
        /// к точке: цель непроходима — сервер берёт проходимую клетку из радиуса, который сам же и назвал при
        /// входе (<see cref="ConnectController.passable_search_radius"/>), а не найдя ни одной, не делает ничего. Потому клик В СТЕНУ и не отбрасывается:
        /// сервер подводит персонажа к ней вплотную, и это ожидаемый игроком исход. Отсекается только клик
        /// вглубь сплошной непроходимой области — за край мира, внутрь скалы, в недоступную соседнюю карту.
        ///
        /// Незнание читается как «пройти можно» (MapController.IsKnownImpassableCell): ошибаться допустимо лишь
        /// в сторону лишней отправки, никогда — в сторону пропущенного клика.
        /// </summary>
        private bool CanServerWalkTo(Vector3 point)
        {
            Vector2Int cell = Cell(point);

            if (!IsKnownImpassableCell(cell))
                return true;

            Vector2Int me = Cell(player.position);

            int radius = ConnectController.passable_search_radius;

            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Саму клетку клика уже проверили выше, а ту, где игрок стоит, целью не берёт и сервер.
                    if (dx == 0 && dy == 0)
                        continue;

                    Vector2Int candidate = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (candidate == me)
                        continue;

                    if (!IsKnownImpassableCell(candidate))
                        return true;
                }

            return false;
        }

        /// <summary>
        /// Опрос управления и отправка команд движения. Зовётся из кадра отрисовки (см. Update), а не из кадра
        /// расчёта физики: ввод в Unity положено читать по кадрам, иначе нажатия между кадрами физики теряются
        /// либо считываются дважды. Отзывчивость управления при этом растёт вместе с частотой экрана.
        /// </summary>
        private void HandleMovementInput()
        {
            if (player != null && player.action != ACTION_REMOVE)
            {
                try
                {
                    vertical = InputSource.GetAxis("Vertical") != 0 ? InputSource.GetAxis("Vertical") : joystick.Vertical;
                    horizontal = InputSource.GetAxis("Horizontal") != 0 ? InputSource.GetAxis("Horizontal") : joystick.Horizontal;

                    // Палец лёг на джойстик, а с места его не повели — игрок просит ВСТАТЬ: шаг с нулевым
                    // направлением снимает на сервере действующую команду движения, и маршрут по клику, и
                    // ход джойстиком. Иначе встать, не сделав шага, нечем — отпущенный джойстик оставляет
                    // персонажа идти по прежней команде. Порог тот же, что у отправки шага ниже: пока палец
                    // не вышел за круг, ходьбы по джойстику нет вовсе, и касание значит только остановку.
                    if (joystick_touched)
                    {
                        joystick_touched = false;

                        if (Math.Abs(horizontal) <= WALK_THRESHOLD && Math.Abs(vertical) <= WALK_THRESHOLD)
                        {
                            WalkResponse stop = new WalkResponse();

                            stop.x = 0;
                            stop.y = 0;
                            stop.Send();
                        }
                    }

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
                            if (Math.Abs(horizontal) > WALK_THRESHOLD || Math.Abs(vertical) > WALK_THRESHOLD)
                            {
                                // не путать импульс нажатия кнопки в определенном направлении с forward (направлением движения, т.е нормальизованным вектором)
                                Vector3 vector = new Vector3(horizontal, vertical, 0).normalized;

                                // Направление клиент не подправляет: обтеканием преграды заведует СЕРВЕР
                                // (move/walk/index — шаг прямо, по одной оси, обход угла диагональю,
                                // подползание вплотную). Не шлём лишь заведомо холостое: направление, где
                                // КАЖДАЯ серверная ветка упирается в известную клиенту преграду (CanServerStep) —
                                // такую команду сервер молча отбрасывает, а слались бы они каждый шаг упора.
                                // Проверка считается по геометрии заново на каждой отправке и состояния
                                // «заблокировано» не держит: смена направления и любой сдвиг позиции (в том
                                // числе серверные подползание и обход угла) сами открывают её обратно.
                                // Опираться на факт НЕПОДВИЖНОСТИ (позиция не сдвинулась с прошлой отправки)
                                // нельзя: эхо позиции приходит с задержкой и ложно читается как «упёрлись» —
                                // застревание у стен и углов. Решает только геометрия преград.
                                // значение forward не сменится (тк его меняет только сервер) но запустится анимация при которой графика персонажа повернется
                                if (DateTime.Compare(block_forward, DateTime.Now) < 1)
                                   player.Forward = vector;

                                if (CanServerStep(vector))
                                {
                                    WalkResponse response = new WalkResponse();

                                    response.x = Math.Round(vector.x, position_precision);
                                    response.y = Math.Round(vector.y, position_precision);
                                    response.Send();
                                }
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
                        block_forward = DateTime.Now.AddSeconds(player.EventTimeout(WalkResponse.GROUP));
                    }
                }
                catch (Exception ex)
                {
                    Error("Ошибка управелния игроком: ", ex);
                }
            }
        }

        /// <summary>
        /// Пройдёт ли шаг в направлении vector хоть одной веткой серверного шага (move/walk/index): прямо на
        /// шаг, по одной из осей, обходом угла по диагонали (её сервер пробует только у ортогонального
        /// направления и засчитывает лишь в СОСЕДНЕЙ клетке), подползанием вплотную к преграде.
        ///
        /// false — все ветки упираются в ИЗВЕСТНУЮ клиенту преграду. Клиент знает только преграды
        /// (IsColliderCell): непроходимой сервер считает и клетку без тайла в слоях, и клетку недоступной
        /// соседней карты, о чём клиенту неизвестно. Потому ветка закрывается ТОЛЬКО известной преградой, а
        /// любое незнание читается как «пройти можно»: ошибаться допустимо лишь в сторону лишней отправки,
        /// никогда — в сторону пропущенного шага.
        ///
        /// Точка отсчёта — авторитетная серверная позиция сущности, в той же местной системе координат карты,
        /// что и коллайдеры. Округление до клетки — банковское (Mathf.RoundToInt), тем же правилом сервер
        /// переводит позицию в тайл.
        /// </summary>
        private bool CanServerStep(Vector3 vector)
        {
            // сервер считает шаг по тем же округлённым составляющим, что клиент кладёт в пакет
            float fx = (float)Math.Round(vector.x, position_precision);
            float fy = (float)Math.Round(vector.y, position_precision);

            int map = player.map;

            // Считаем от СЕРВЕРНОЙ позиции (EntityModel.position): от неё сервер и ведёт свой расчёт шага.
            // Позиция аватара в transform — сглаженная, её догоняет корутина движения и двигает экстраполяция,
            // и на ней предикат отвечал бы про точку, где сервера уже (или ещё) нет.
            Vector3 position = player.position;
            Vector2Int cell = Cell(position);

            // шаг прямо в направлении
            if (!IsKnownCollider(map, position + new Vector3(fx, fy, 0) * step))
                return true;

            // только по X либо только по Y
            if (fx != 0 && !IsKnownCollider(map, position + new Vector3(fx, 0, 0) * step))
                return true;

            if (fy != 0 && !IsKnownCollider(map, position + new Vector3(0, fy, 0) * step))
                return true;

            // обход угла — только у ортогонального направления
            if (fx == 0 || fy == 0)
            {
                Vector3 first;
                Vector3 second;

                // знак второй оси: сперва та сторона, куда игрок уже смещён относительно центра своей клетки
                if (fy == 0)
                {
                    float prefer = position.y >= Mathf.Round(position.y) ? corner_offset : -corner_offset;

                    first = position + new Vector3(fx * corner_offset, prefer, 0) * step;
                    second = position + new Vector3(fx * corner_offset, -prefer, 0) * step;
                }
                else
                {
                    float prefer = position.x >= Mathf.Round(position.x) ? corner_offset : -corner_offset;

                    first = position + new Vector3(prefer, fy * corner_offset, 0) * step;
                    second = position + new Vector3(-prefer, fy * corner_offset, 0) * step;
                }

                if (Cell(first) != cell && !IsKnownCollider(map, first))
                    return true;

                if (Cell(second) != cell && !IsKnownCollider(map, second))
                    return true;
            }

            // подползание: по каждой упирающейся оси встаём на creep_depth от центра своей клетки в сторону
            // преграды. Сервер отбивает его, когда игрок в этой точке уже стоит (дистанция чебышевская).
            Vector3 creep = new Vector3(
                fx > 0 ? cell.x + creep_depth : (fx < 0 ? cell.x - creep_depth : position.x),
                fy > 0 ? cell.y + creep_depth : (fy < 0 ? cell.y - creep_depth : position.y),
                position.z);

            if (Mathf.Max(Mathf.Abs(creep.x - position.x), Mathf.Abs(creep.y - position.y)) >= 0.01f
                && !IsKnownCollider(map, creep))
                return true;

            return false;
        }

        // Известна ли клиенту преграда в клетке точки. Ответ false значит «преграды не знаю», а не «проходимо»
        // (см. CanServerStep).
        private bool IsKnownCollider(int map, Vector3 point)
        {
            return IsColliderCell(map, Cell(point));
        }

        private Vector2Int Cell(Vector3 point)
        {
            return new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
        }

        // множитель кольца к рендер-границам трупа: 1.0 — ровно по спрайту (прозрачные поля текстуры
        // сами дают небольшой зазор). Держать МЕНЬШЕ зазора кликабельного коллайдера трупа
        // (ObjectModel.CORPSE_HIT_GAP) — кольцо лежит ВНУТРИ хит-области, клик по кольцу попадает по трупу.
        private const float HIGHLIGHT_GAP = 1.0f;

        /// <summary>
        /// Hover-фидбек: навёл курсор на кликабельную сущность (контейнер добычи — труп либо сундук) —
        /// кольцо ВОКРУГ её спрайта (сигнал «кликабельно» + отличие контейнера от пустой земли под
        /// курсором). Курсор ушёл — скрыть. Один переиспользуемый объект (двигаем/масштабируем, не пересоздаём).
        /// </summary>
        private void UpdateHoverHighlight(Vector3 screenPos)
        {
            // Под элементом интерфейса кольца нет: нажатие туда до мира не доходит (см. разбор нажатия в
            // Update), а кольцо обещает игроку ровно этот клик. Тем же признаком снимается и мировой луч —
            // пока указатель над интерфейсом, искать под ним контейнер незачем.
            ObjectModel hovered = ScreenUiAtPointer(screenPos) != null ? null : ContainerAtScreen(screenPos);
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
        /// Кликабельный контейнер добычи (труп существа либо объект-сундук) под курсором. Первая значимая
        /// (НЕ-игрок) сущность по лучу решает тип взаимодействия: живой враг / предмет сверху → null
        /// (не подсвечиваем). Если она контейнер — среди ВСЕХ контейнеров под курсором выбираем
        /// БЛИЖАЙШИЙ к точке курсора (центры визуальных границ): у сваленных в кучу тел коллайдеры
        /// перекрываются, а порядок RaycastAll при точечном луче недетерминирован — без выбора по
        /// дистанции кольцо «прыгало бы» на соседнее тело.
        /// Тем же методом контейнер выбирает и клик (Update) — подсветка и клик всегда об одной сущности.
        /// </summary>
        private ObjectModel ContainerAtScreen(Vector3 screenPos)
        {
            if (Camera.main == null || player == null) return null;

            Vector3 world = Camera.main.ScreenToWorldPoint(screenPos);
            RaycastHit2D[] hits = Physics2D.RaycastAll(world, Vector2.zero, Mathf.Infinity);

            bool containerZone = false;         // первая значимая сущность — контейнер?
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
                bool container = obj != null && LootWindowController.IsContainer(obj);

                if (!containerZone)
                {
                    if (!container)
                        return null;            // сверху живой/предмет — боевой/подборный клик в приоритете
                    containerZone = true;
                }

                if (!container) continue;

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
            // Scale курсора = scale видимой иконки: icon.localScale=1, курсор берёт размер из собственного
            // rect — серверный size предмета в UI не участвует. Если Icon=null — scale от image (тоже 1).
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

        /// <summary>
        /// Отпустить всё, что держит курсор: сам предмет, слот-источник экипировки, подсветку допустимых
        /// слотов и картинку. Состояние «в руке» размазано по нескольким полям, и снимать их порознь —
        /// значит рано или поздно забыть одно: одна точка освобождения на все пути (отпустил, потерял
        /// предмет, отменил). Сам предмет тут не трогаем: он живёт в своём слоте, курсор лишь рисует его
        /// копию, а истинное положение вещей приезжает дельтой сервера.
        /// </summary>
        private void ReleaseCursor()
        {
            MyMoveable = null;
            SourceEquipmentSlot = null;
            EquipmentController.ClearHighlight();
            cursor.color = new Color(0, 0, 0, 0);
            cursor.raycastTarget = false;
        }
    }
}
