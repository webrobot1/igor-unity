using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WebGLSupport;

namespace Mmogick
{
    /// <summary>
	/// Класс для обновления Меню настрое игрока
	/// </summary>
    abstract public class SpellBookController : UIController
    {
        /// <summary>Компонент префаба заклинания: группа команды, которой оно применяется.</summary>
        public const string COMPONENT_EVENT = "event";

        /// <summary>Компонент префаба заклинания: стоимость применения в мане.</summary>
        public const string COMPONENT_MP_COST = "mp_cost";

        /// <summary>Раздел справочника книги: заклинание → название его стихии.</summary>
        public const string SECTION_SPELL = "spell";

        /// <summary>Раздел справочника книги: стихия → её цвет в интерфейсе, вида «#RRGGBB».</summary>
        public const string SECTION_ELEMENT = "element";

        /// <summary>
        /// Компонент существа: какие заклинания у него есть (ключ — префаб заклинания, значение — true).
        /// У своего игрока состав приходит пакетом, у чужой цели — нет: компонент приватный, и о ней
        /// известно лишь типовое для её вида из каталога префабов.
        /// Умолчание же этого компонента — справочник «заклинание → название стихии», один на всю игру:
        /// значение говорит о КОНКРЕТНОМ существе, умолчание — о контенте, одинаковом для всех.
        /// </summary>
        public const string COMPONENT_SPELL_BOOK = "spell_book";

        /// <summary>
        /// Раздел справочника стихий — умолчания компонента книги. Умолчание книгой не является: это
        /// справочник, одинаковый для всей игры, и разделов в нём два — состав школ и их цвета.
        /// Раздела нет (справочник ещё старой формы либо его не завели) — null, и каждый потребитель
        /// решает сам: без состава школ заклинание идёт во вкладку без подписи, без цвета стихия
        /// красится по своему названию.
        /// </summary>
        public static JObject ElementDirectory(string section)
        {
            JObject directory = AnimationCacheService.GetComponentDefault(COMPONENT_SPELL_BOOK) as JObject;

            return directory != null ? directory[section] as JObject : null;
        }

        [Header("Для работы с книгой заклинаний")]

        /// <summary>
        /// префаб заклинания в книге
        /// </summary>
        [SerializeField]
        private Spell spellPrefab;

        /// <summary>
        /// префаб заклинания в книге
        /// </summary>
        [SerializeField]
        private Transform spellGroupArea;

        /// <summary>
        /// префаб вкладки — группы команд, которой применяются её заклинания
        /// </summary>
        [SerializeField]
        private SpellTab tabPrefab;

        /// <summary>
        /// лента вкладок над списком заклинаний: вкладок столько, сколько групп в книге,
        /// поэтому лента прокручивается вбок, а ширина каждой вкладки — по её подписи
        /// </summary>
        [SerializeField]
        private Transform tabGroupArea;

        /// <summary>
        /// список доступных заклинаний с их характеристиками
        /// </summary>
        private static Dictionary<string, Spell> _spells;

        /// <summary>
        /// вкладки, созданные по стихиям заклинаний книги
        /// </summary>
        private List<SpellTab> _tabs;

        /// <summary>
        /// стихия открытой вкладки — держится между пересборками книги, чтобы приход пакета
        /// не сбрасывал игрока на первую вкладку
        /// </summary>
        private string _activeElement;

        public Dictionary<string, Spell> Spells
        {
            get { return _spells; }
            set { }
        }

        protected override void Awake()
        {
            base.Awake();
           
            // объявлять тут тк мы используем в unity Editor опцию при который вызод из play моде НЕ очищает статику (зато быстро выходит и заходит, но надо очищать вручную везде в Awake)
            _spells = new Dictionary<string, Spell>();
            _tabs = new List<SpellTab>();

            if (spellPrefab == null)
            {
                Error("не указан префаб заклинания в книге");
                return;
            }

            if (spellGroupArea == null)
            {
                Error("не указан Transform книги на которую буду загружаться с сервера заклинаний");
                return;
            }

            if (!spellGroupArea.IsChildOf(spellGroup.transform))
            {
                Error("указанный объект Transform книги заклинаний книги на которую буду загружаться с сервера заклинаний не является часть CanvasGroup указанной как книга заклинаний");
                return;
            }

            if (tabPrefab == null)
            {
                Error("не указан префаб вкладки книги заклинаний");
                return;
            }

            if (tabGroupArea == null)
            {
                Error("не указан Transform ряда вкладок книги заклинаний");
                return;
            }

            if (!tabGroupArea.IsChildOf(spellGroup.transform))
            {
                Error("указанный объект Transform ряда вкладок не является частью CanvasGroup указанной как книга заклинаний");
                return;
            }
        }

        /// <summary>
        /// Книга собирается из ДВУХ источников: состав — компонент spell_book своего игрока (какие заклинания
        /// выучены), а чем каждое является (название, описание, стоимость маны, группа команды) — каталог
        /// префабов, который клиент тянет до входа в мир: заклинание там обычный префаб, и его свойства лежат
        /// рядом со свойствами прочего контента. Стихия — исключение: она одинакова для всех и приходит
        /// справочником в умолчании компонента книги, а не свойством каждого заклинания.
        ///
        /// Собирается в HandleData, до обхода сущностей пакета: панель быстрых действий ищет свои заклинания
        /// в готовой книге, а её контроллер стоит в цепочке ВЫШЕ этого — на обходе сущностей книга была бы
        /// ещё пуста, и панель ругалась бы на «не найдено заклинание».
        /// </summary>
        protected override void HandleData(NewRecive<PlayerRecive, CreatureRecive> recive)
        {
            PlayerRecive player = FindOwnPlayer(recive);

            if (player != null && player.components != null)
            {
                Dictionary<string, bool> book = player.components.spell_book;
                if (book != null)
                {
                    _spells = new Dictionary<string, Spell>();
                    foreach (Transform child in spellGroupArea)
                    {
                        Destroy(child.gameObject);
                    }

                    // Стихия живёт не у самого заклинания, а справочником в умолчании компонента книги —
                    // он один на всю игру, потому и читается разом до обхода.
                    JObject elements = ElementDirectory(SECTION_SPELL);

                    foreach (var spell in book)
                    {
                        if (!spell.Value)
                            continue;

                        JToken apply = AnimationCacheService.GetComponentValue(spell.Key, COMPONENT_EVENT, null);
                        JToken cost  = AnimationCacheService.GetComponentValue(spell.Key, COMPONENT_MP_COST, null);

                        string group = apply != null ? apply.Value<string>() : null;

                        if (string.IsNullOrEmpty(group))
                        {
                            // Сервер держит заклинание в книге игрока, а в нашем каталоге его свойств нет — каталог
                            // разошёлся с сервером. Сбрасываем его, чтобы следующий заход перекачал целиком: без
                            // этого игрок попадает в петлю «вход → та же ошибка → вход», ведь дельта по дате
                            // недостающую запись уже не принесёт. Тот же приём, что у ошибок самой синхронизации
                            // (SigninController: ResetCache + Error). Ошибку всё равно показываем — рассинхрон
                            // чинится у источника, здесь только снимается тупик.
                            AnimationCacheService.ResetCache(GAME_ID);
                            Error("У заклинания " + spell.Key + " не задана группа команды (компонент " + COMPONENT_EVENT + "). Каталог сброшен — повторите вход");
                            return;
                        }

                        Spell prefab = Instantiate(spellPrefab, spellGroupArea) as Spell;

                        prefab.Magic = spell.Key;
                        prefab.@event = group;

                        // Стихия необязательна: заклинание без неё собирается во вкладку без подписи,
                        // а не выпадает из книги — иначе выученное заклинание пропало бы у игрока молча.
                        JToken element = elements != null ? elements[spell.Key] : null;
                        prefab.Element = element != null ? element.Value<string>() : "";

                        prefab.title.text = AnimationCacheService.GetPrefabName(spell.Key) ?? spell.Key;
                        prefab.description.text = AnimationCacheService.GetPrefabDescription(spell.Key) ?? "";
                        prefab.ManaCost = cost != null ? cost.Value<int>() : 0;

                        _spells.Add(spell.Key, prefab);
                    }

                    RebuildTabs();
                }
            }

            base.HandleData(recive);
        }

        /// <summary>
        /// Вкладка на каждую стихию, что фактически есть в книге игрока: пустых вкладок не бывает, а
        /// выученное заклинание новой стихии заводит вкладку само — перечень стихий нигде не задан, он
        /// целиком следствие книги. Подпись — само название стихии, как оно записано в справочнике.
        /// Открытая вкладка сохраняется, пока её стихия в книге есть; исчезла — открывается первая.
        /// </summary>
        private void RebuildTabs()
        {
            foreach (Transform child in tabGroupArea)
            {
                Destroy(child.gameObject);
            }

            _tabs = new List<SpellTab>();

            List<string> elements = new List<string>();
            foreach (var spell in _spells)
            {
                if (!elements.Contains(spell.Value.Element))
                    elements.Add(spell.Value.Element);
            }

            elements.Sort();

            if (!elements.Contains(_activeElement))
                _activeElement = elements.Count > 0 ? elements[0] : null;

            foreach (string element in elements)
            {
                SpellTab tab = Instantiate(tabPrefab, tabGroupArea) as SpellTab;
                tab.Setup(element, SelectTab);
                _tabs.Add(tab);
            }

            ApplyTab();
        }

        private void SelectTab(SpellTab tab)
        {
            _activeElement = tab.Element;
            ApplyTab();
        }

        /// <summary>
        /// Показ открытой вкладки. Карточки чужих стихий выключаются целиком — их иконки и остаток отката
        /// перестают считаться, и панель быстрых действий берёт доступность своим счётом
        /// (<see cref="MoveableObject.IsUnavailable"/>), а не цветом карточки.
        /// </summary>
        private void ApplyTab()
        {
            foreach (SpellTab tab in _tabs)
            {
                tab.SetSelected(tab.Element == _activeElement);
            }

            foreach (var spell in _spells)
            {
                spell.Value.gameObject.SetActive(spell.Value.Element == _activeElement);
            }
        }

        /// <summary>
        /// Свой игрок в пакете мира: он приходит внутри своей карты, а карт в пакете несколько (соседние
        /// локации открытого мира). null — в этом пакете своего игрока нет (обычная дельта чужих сущностей).
        /// </summary>
        private PlayerRecive FindOwnPlayer(NewRecive<PlayerRecive, CreatureRecive> recive)
        {
            if (recive.world == null)
                return null;

            foreach (var map in recive.world)
                if (map.Value != null && map.Value.player != null && map.Value.player.TryGetValue(player_key, out PlayerRecive player))
                    return player;

            return null;
        }
    }
}