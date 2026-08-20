using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using WebGLSupport;

namespace Mmogick
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class Spell: MoveableObject, IPointerClickHandler, ITakeable
    {
        /// <summary>Цвет подложки заклинания без стихии: у него нет школы, и оттенок ни о чём не говорит.</summary>
        private static readonly Color NO_ELEMENT_COLOR = new Color(0.62f, 0.66f, 0.74f);

        public Text title;

        /// <summary>Группа команды, которой заклинание применяется («fight/bolt»).</summary>
        public string @event;

        public Text description;

        [SerializeField] private Text mp;
        public Text remain;

        /// <summary>
        /// Подложка под иконкой: свечение в цвете стихии. Иконки приходят с сервера с прозрачным фоном,
        /// и без неё карточки читаются как ряд картинок на пустом месте, а стихия видна только по вкладке.
        /// </summary>
        [SerializeField] private Image background;

        private string _magic;

        private string _element;

        /// <summary>
        /// Стоимость применения в мане. Носитель — само число, надпись лишь его отражает.
        ///
        /// Обратный порядок (надпись как источник, разбор строки на каждом чтении) не годится сразу
        /// по двум причинам. Стоимость спрашивают КАЖДЫЙ кадр — панель быстрых действий гасит по ней
        /// недоступные иконки, — и разбор строки там стоит дороже самого значения. И он падает
        /// разбором на пустой надписи: карточка, которую ещё не наполнили, несёт пустой Text, а
        /// у собранного билда это уже не диагностика, а вылет.
        /// </summary>
        private int _manaCost;

        public override int ManaCost
        {
            get { return _manaCost; }
            set
            {
                _manaCost = value;

                if (mp != null)
                    mp.text = value.ToString();
            }
        }

        public string Magic
        {
            get
            {
                return _magic;
            }
            set
            {
                _magic = value;

                // Иконка из серверной library + server-size scale (см. MoveableObject.ApplyPrefabImage).
                // Корневой image получает sprite (нужен для LayoutGroup), видимый icon-child
                // получает sprite + localScale = 1/serverSize.
                ApplyPrefabImage(value);
            }
        }

        /// <summary>
        /// Стихия заклинания — по ней книга раскладывает его по вкладкам, и в её цвете горит подложка
        /// под иконкой. Пустая строка — заклинание, которого нет в справочнике стихий.
        /// </summary>
        public string Element
        {
            get
            {
                return _element;
            }
            set
            {
                _element = value;

                if (background == null)
                {
                    ConnectController.Error("не найден объект background в для элемента Заклинания в книге");
                    return;
                }

                background.color = ElementColor(value);
            }
        }

        /// <summary>
        /// Цвет стихии. Задаёт его сама игра — разделом стихий того же справочника, что раскладывает
        /// заклинания по школам (умолчание компонента книги, см. <see cref="SpellBookController"/>):
        /// названия стихий свободные, их заводят и переименовывают в админке, и перечень на клиенте
        /// разошёлся бы с игрой молча. Текстура подложки идёт от светлого центра к приглушённым краям,
        /// а цвет её умножает — оттого одного цвета хватает и на свечение в середине, и на затемнение
        /// по краю.
        /// </summary>
        private static Color ElementColor(string element)
        {
            if (string.IsNullOrEmpty(element))
                return NO_ELEMENT_COLOR;

            JObject colors = SpellBookController.ElementDirectory(SpellBookController.SECTION_ELEMENT);
            JToken own = colors != null ? colors[element] : null;

            if (own != null && ColorUtility.TryParseHtmlString(own.Value<string>(), out Color chosen))
                return chosen;

            // Стихии в справочнике нет либо цвет записан не как #RRGGBB: оттенок считаем из самого
            // названия. Он произволен, но устойчив — одна и та же стихия красится одинаково у всех своих
            // заклинаний и между входами, а новая стихия попадает в книгу не серой.
            int hash = 0;
            foreach (char letter in element)
                hash = hash * 31 + letter;

            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.55f, 1f);
        }



        protected override void Awake()
        {
            // Error() не бросает: без return карточка доехала бы до работы с этими null'ами
            // (title/description — в GetTooltipText, mp — в ManaCost).
            if (title == null)
            {
                ConnectController.Error("не найден объект title в для элемента Заклинания в книге");
                return;
            }

            if (description == null)
            {
                ConnectController.Error("не найден объект description в для элемента Заклинания в книге");
                return;
            }

            if (mp == null)
            {
                ConnectController.Error("не найден объект mana в для элемента Заклинания в книге");
                return;
            }

            base.Awake();
        }

        /// <summary>
        /// Название, описание и стоимость — те же, что стоят на самой карточке заклинания в книге: своего
        /// источника у неё нет, карточку наполняет книга. Роли строк размечает <see cref="TextStyle"/>,
        /// как и у прочих подсказок.
        ///
        /// Спрашивает этот текст только панель быстрых действий: там от заклинания видна одна иконка.
        /// Сама книга подсказку по наведению НЕ показывает намеренно — карточка уже несёт название,
        /// описание и стоимость строками, и подсказка повторяла бы их поверх них же.
        /// </summary>
        public override string GetTooltipText()
        {
            return TextStyle.Title(title.text)
                + "\n" + TextStyle.Hint(description.text)
                + "\n" + TextStyle.Value("Мана: " + ManaCost);
        }



        public override bool IsOnCooldown()
        {
            return PlayerController.Player != null && PlayerController.Player.GetEventRemain(@event) > 0;
        }

        /// <summary>
        /// Лечить нечего: заклинание лечебное, а запас здоровья уже полон. Зеркалит серверный отказ — там такое
        /// применение гаснет, не тронув ману, — поэтому иконка гасится, как при нехватке маны, и клик не уходит.
        /// Цель тут своя: лечение чужого игрока идёт кликом по нему, и полнота ЕГО запаса решается на сервере.
        /// </summary>
        private bool NothingToHeal
        {
            get
            {
                return @event == HealResponse.GROUP
                    && PlayerController.Player != null
                    && PlayerController.Player.hpMax > 0
                    && PlayerController.Player.hp >= PlayerController.Player.hpMax;
            }
        }

        public override bool IsUnavailable()
        {
            return PlayerController.Player == null
                || PlayerController.Player.hp <= 0
                || ManaCost > PlayerController.Player.mp
                || NothingToHeal;
        }

        public override (float fillAmount, float remainSeconds) GetCooldownProgress()
        {
            if (PlayerController.Player == null) return (0f, 0f);
            double remainTime = PlayerController.Player.GetEventRemain(@event);
            if (remainTime <= 0) return (0f, 0f);
            double timeout = PlayerController.Player.EventTimeout(@event);
            float fill = timeout > 0 ? (float)(remainTime / timeout) : 0f;
            return (fill, (float)remainTime);
        }

        protected void FixedUpdate()
        {
            if (PlayerController.Player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE)
            {
                bool onCooldown = IsOnCooldown();
                bool unavailable = IsUnavailable();

                remain.text = onCooldown ? PlayerController.Player.GetEventRemain(@event) + " сек." : "0 сек.";

                // Cooldown alpha — на видимый icon (если есть), иначе fallback на корневой image.
                // Корневой image держит alpha=0 (prefab), мигрировать её на icon — единственный
                // способ показать затемнение пользователю; ActionBar мирорит icon.color (см. ActionBar.FixedUpdate).
                Image alphaTarget = icon != null ? icon : image;
                alphaTarget.color = new Color(alphaTarget.color.r, alphaTarget.color.g, alphaTarget.color.b, unavailable ? 0.5f : 1f);
                image.raycastTarget = true;
            } 
        }

        /// <summary>
        /// Взять заклинание на курсор (чтобы положить его в панель быстрых действий) можно, лишь пока
        /// его вообще есть чем применить: не хватает маны либо идёт откат — нажатие ничего не делает.
        /// Тем же признаком указатель принимает форму руки — обещание руки и исход нажатия обязаны совпадать.
        /// </summary>
        public bool CanTake
        {
            get
            {
                // Занятый курсор тут не помеха: нажатие по карточке при полном курсоре меняет то, что
                // в нём лежит (см. CursorController.Update, chain-swap), — заклинание берётся и так.
                return PlayerController.Player != null
                    && ManaCost <= PlayerController.Player.mp
                    && PlayerController.Player.GetEventRemain(@event) <= 0;
            }
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (CanTake)
            {
                CursorController.TakeMoveable(this);
            }
        }

        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            if(obj != null && obj.GetComponent<ActionBar>())
            {
                ActionBar bar = obj.GetComponent<ActionBar>();
                ActionBarsResponse response = new ActionBarsResponse();

                if (bar.Item != this)
                {
                    Debug.Log("Быстрая клавиша " + bar.num + ": отправим на сервер установку заклинания " + Magic);
                    response.actionbars.Add(bar.num, new ActionBarsRecive("spell", Magic));
                }
                else
                {
                    Debug.LogWarning("Быстрая клавиша " + bar.num + ": Попытка установить одинаковые значение - очищаем ячейку");
                    response.actionbars.Add(bar.num, null);
                }
                response.Send();
            }
            else
            {
                if(ManaCost <= PlayerController.Player.mp)
                {
                    Debug.Log("Используем заклинание "+ Magic);
                    switch (@event)
                    {
                        case "fight/bolt":
                            BoltResponse response = new BoltResponse();
                            response.spell = Magic;

                            // Стрелять можно лишь по тому, у кого есть запас здоровья: сундук, лавка, портал
                            // и лежащая вещь целью выбираются (окно сведений рассказывает и о них), но
                            // выстрел по ним сервер отбивает молча, а пауза заклинания у игрока уже пошла бы.
                            ObjectModel clicked = obj != null ? obj.GetComponent<ObjectModel>() : null;
                            EnemyModel shootable = clicked as EnemyModel;
                            EnemyModel chosen = MainController.Instance.Target as EnemyModel;

                            if (shootable != null && shootable.hp > 0)
                            {
                                response.target = shootable.key;

                                // Выбранной целью становится тот, ПО КОМУ кликнули: следующий выстрел с панели
                                // быстрых действий пойдёт по ней же, без повторного клика по существу.
                                if(MainController.Instance.Target == null)
                                    MainController.Instance.Target = shootable;
                            }
                            else if (chosen != null && chosen.hp > 0)
                            {
                                response.target = chosen.key;
                            }
                            else if(pos != Vector2.zero)
                            {
                                PlayerController.Player.Forward = new Vector3(pos.x, pos.y, PlayerController.Player.Forward.z);

                                response.x = Math.Round(pos.x, PlayerController.position_precision);
                                response.y = Math.Round(pos.y, PlayerController.position_precision);
                            }
                            else
                            {
                                // без цели и без направления — стреляем по forward игрока
                                response.x = Math.Round(PlayerController.Player.Forward.x, PlayerController.position_precision);
                                response.y = Math.Round(PlayerController.Player.Forward.y, PlayerController.position_precision);
                            }

                            response.Send();
                        break;
                        case "magic/heal":
                            HealResponse heal = new HealResponse();
                            heal.spell = Magic;

                            // Лечить себя при полном запасе нечего — сервер такую команду гасит, не тронув ману.
                            // Проверка тут, а не только в подсветке иконки: применение приходит и с панели
                            // быстрых действий, и кликом по себе, и обе двери ведут сюда.
                            if (NothingToHeal && (obj == null || obj.GetComponent<PlayerModel>() == PlayerController.Player))
                            {
                                Debug.Log("Заклинание " + Magic + ": запас здоровья полон, лечить нечего");
                                return;
                            }

                            if (obj != null && obj.GetComponent<ObjectModel>() != null)
                            {
                                // Сервер лечит только игроков: по мобу и объекту команду не отправляем вовсе,
                                // иначе она молча гасится у сервера, а пауза заклинания у игрока уже пошла бы.
                                if (obj.GetComponent<PlayerModel>() == null)
                                {
                                    Debug.LogWarning("Заклинание " + Magic + ": лечить можно только игрока");
                                    return;
                                }

                                heal.target = obj.GetComponent<ObjectModel>().key;
                            }
                            // Выбранная ранее цель берётся только при запуске с панели быстрых действий (pos пустой):
                            // клик в мире мимо существа — намеренное лечение себя, и старая цель его перебивать не должна.
                            else if (pos == Vector2.zero && MainController.Instance.Target is PlayerModel)
                            {
                                heal.target = MainController.Instance.Target.key;
                            }

                            heal.Send();
                        break;
                        default:
                            ConnectController.Error("неизвестный тип группы "+ @event+" у заклинания "+Magic);
                        break;
                    }
                }
                else
                    Debug.LogError("Недостаточно маны для заклинания " + Magic);
            }
        }
    }
}