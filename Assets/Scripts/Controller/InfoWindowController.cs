using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Окно сведений о выбранной цели: кто это, как выглядит в деле и что о нём известно. Открывается
    /// иконкой у рамки цели, наполняется тем, кто в рамке сейчас, и закрывается вместе с потерей цели —
    /// сведения без своего носителя читались бы как чужие.
    ///
    /// Что показывает: имя (у игрока — логин аккаунта), портрет с перебором действий существа,
    /// описание его префаба из библиотеки, характеристики, особенности, заклинания и отметки времени
    /// с сервера. У игрока к ним добавляется адрес подключения, а сами отметки читаются иначе:
    /// заведение записи — это регистрация, последнее изменение — последний вход в игру.
    ///
    /// Что именно попадает в характеристики и особенности, задают перечни <see cref="STATS"/> и
    /// <see cref="TRAITS"/>: новое свойство добавляют записью в них, показ перебирает перечень и о самих
    /// свойствах ничего не знает.
    /// </summary>
    abstract public class InfoWindowController : DebugPanelController
    {
        /// <summary>Команда существа: восстановление здоровья. Её срок и есть частота восстановления.</summary>
        private const string GROUP_REGENERATION_HP = "status/regenerationhp";

        /// <summary>То же для маны.</summary>
        private const string GROUP_REGENERATION_MP = "status/regenerationmp";

        /// <summary>Команда контейнера: её срок — как часто он возвращает вынесенное к своему составу.</summary>
        private const string GROUP_CONTAINER = "object/container";

        /// <summary>
        /// Имена данных команд восстановления: сколько прибавляется за раз. Величина приходит вместе с
        /// самой командой; своего значения на этот случай у клиента нет и быть не может — умолчание
        /// живёт в параметрах команды на сервере, где его правит гейм-дизайнер.
        /// </summary>
        private const string DATA_LIFE = "life";
        private const string DATA_MANA = "mana";

        /// <summary>Множители торговца к базовой цене предмета — пара buy/sell (см. TradeLine).</summary>
        private const string COMPONENT_TRADE = "trade";

        /// <summary>
        /// Характеристики — числа, которыми существо меряют. Запас (здоровье, мана) идёт парой
        /// «текущее из максимума»: обе половины разрешаются одним правилом.
        /// </summary>
        private static readonly InfoLine[] STATS =
        {
            new InfoLine(EnemyModel.COMPONENT_HP, "Здоровье", Amount, pairKey: EnemyModel.COMPONENT_HP_MAX),

            // Ману сервер рассылает только своему игроку — чужую видеть не полагается. Типовое значение
            // вида в справочнике при этом есть, поэтому строку держим на присланном (ownOnly): иначе у
            // любого моба показалось бы умолчание справочника, выданное за сведения о нём.
            new InfoLine(EnemyModel.COMPONENT_MP, "Мана", Amount, pairKey: EnemyModel.COMPONENT_MP_MAX, ownOnly: true),

            // Скорость: главное — игровое число, за ним в скобках та скорость, которой существо шло у
            // нас на глазах. Величины разные и одна другую не заменяет: срок шага сервер считает своей
            // формулой, её правят в админке, и наш пересчёт разошёлся бы с ней молча. Существо при нас
            // не ходило — скобок нет вовсе.
            new InfoLine(EnemyModel.COMPONENT_SPEED, "Скорость",
                (game, pace) => pace != null ? Number(game) + " (" + Cells(pace.Value) + " в секунду)" : Number(game),
                observed: WalkPace),
        };

        /// <summary>
        /// Особенности — то, что существо делает само, рассказанное словами: числа тут ничего не
        /// говорят игроку, а поведение говорит.
        /// </summary>
        private static readonly InfoLine[] TRAITS =
        {
            // Ноль у обоих значит «этого существо не делает»: бьётся до конца, зовёт только сам за себя.
            // Так их читает и сервер, поэтому строки в таком случае нет вовсе — «меньше 0%» и «радиус 0
            // клеток» рассказывали бы о поведении, которого не существует.
            new InfoLine(EnemyModel.COMPONENT_FLEE, null,
                (value, pair) => value > 0 ? "Убегает из боя, когда здоровья остаётся меньше " + Share(value) : null),

            new InfoLine(EnemyModel.COMPONENT_ASSIST, null,
                (value, pair) => value > 0 ? "Получив урон, зовёт сородичей на помощь. Радиус — " + Cells(value) : null),

            // Значок берём у запаса, который прибывает: команда своего значка не имеет, а сердце и
            // жемчужина называют прибывающее короче любой подписи. Подпись при этом остаётся — на неё
            // строка возвращается, если картинка не встала, и ею же читается подсказка.
            new InfoLine(GROUP_REGENERATION_HP, "Восстановление здоровья",
                Recovery,
                pairKey: DATA_LIFE, source: InfoSource.Command, iconKey: EnemyModel.COMPONENT_HP),

            new InfoLine(GROUP_REGENERATION_MP, "Восстановление маны",
                Recovery,
                pairKey: DATA_MANA, source: InfoSource.Command, iconKey: EnemyModel.COMPONENT_MP),

            // Срок пополнения контейнера: через него сундук и лавка возвращают вынесенное к заданному
            // виду составу. Величины у команды нет — она приводит состав целиком, а не добавляет по
            // штуке, поэтому говорим только о сроке.
            new InfoLine(GROUP_CONTAINER, null,
                (period, amount) => "Пополняется раз в " + Seconds(period),
                source: InfoSource.Command),
        };

        [Header("Для работы с окном сведений о цели")]

        /// <summary>
        /// само окно сведений
        /// </summary>
        [SerializeField]
        private CanvasGroup infoGroup;

        /// <summary>
        /// иконка у рамки цели, открывающая окно про неё
        /// </summary>
        [SerializeField]
        private Button infoButton;

        /// <summary>
        /// иконка у рамки игрока, открывающая то же окно про него самого. Своего персонажа целью не
        /// выбрать (цель — всегда кто-то другой), поэтому о себе окно открывается отдельной кнопкой.
        /// </summary>
        [SerializeField]
        private Button infoSelfButton;

        /// <summary>
        /// портрет цели в окне: перебирает её действия
        /// </summary>
        [SerializeField]
        private InfoPortrait infoPortrait;

        [SerializeField]
        private Text infoName;

        [SerializeField]
        private Text infoDescription;

        /// <summary>
        /// заголовок блока характеристик: сами они идут списком ниже и гаснут вместе с ним
        /// </summary>
        [SerializeField]
        private Text infoStatsTitle;

        /// <summary>
        /// список характеристик: каждая — отдельная строка, у которой подпись бывает заменена иконкой
        /// </summary>
        [SerializeField]
        private RectTransform infoStatsArea;

        /// <summary>
        /// строка списка характеристик: иконка и текст
        /// </summary>
        [SerializeField]
        private InfoPoint infoStatPrefab;

        /// <summary>
        /// заголовок блока особенностей: сами они идут списком ниже и гаснут вместе с ним
        /// </summary>
        [SerializeField]
        private Text infoTraitsTitle;

        /// <summary>
        /// список особенностей строками: сюда идут те, которым значка не досталось
        /// </summary>
        [SerializeField]
        private RectTransform infoTraitsArea;

        /// <summary>
        /// пункт списка особенностей: значок и текст
        /// </summary>
        [SerializeField]
        private InfoPoint infoTraitPrefab;

        /// <summary>
        /// сетка значков особенностей: особенность, у чьего компонента есть картинка, встаёт значком, а
        /// рассказ о ней читается подсказкой. Отдельная от списка область нужна потому, что значки идут
        /// в ряд, а строки — одна под другой; в общей раскладке одно из двух встало бы неверно.
        /// </summary>
        [SerializeField]
        private RectTransform infoTraitsIconArea;

        /// <summary>
        /// значок одной особенности в сетке: та же строка, что и в списке, только показанная картинкой
        /// </summary>
        [SerializeField]
        private InfoPoint infoTraitIconPrefab;

        /// <summary>
        /// заголовок блока заклинаний: сами они лежат сеткой иконок ниже, и заголовок гаснет вместе с ней
        /// </summary>
        [SerializeField]
        private Text infoSpellsTitle;

        /// <summary>
        /// сетка иконок заклинаний
        /// </summary>
        [SerializeField]
        private RectTransform infoSpellsArea;

        /// <summary>
        /// иконка одного заклинания в сетке
        /// </summary>
        [SerializeField]
        private InfoSpell infoSpellPrefab;

        /// <summary>
        /// заголовок блока добычи: сама она лежит сеткой иконок ниже и гаснет вместе с ним
        /// </summary>
        [SerializeField]
        private Text infoLootTitle;

        /// <summary>
        /// сетка иконок добычи
        /// </summary>
        [SerializeField]
        private RectTransform infoLootArea;

        /// <summary>
        /// иконка одной строки добычи в сетке
        /// </summary>
        [SerializeField]
        private InfoLoot infoLootPrefab;

        /// <summary>
        /// строки об учётной записи и отметках времени
        /// </summary>
        [SerializeField]
        private Text infoDetails;

        /// <summary>
        /// чьи сведения показаны сейчас: по смене того, о ком окно, портрет пересобирается
        /// </summary>
        private ObjectModel _shown;

        /// <summary>
        /// Заклинания, которыми сетка набрана сейчас. Окно наполняется каждый кадр, а иконки — объекты:
        /// пересобираем их, только когда состав действительно сменился.
        /// </summary>
        private readonly List<string> _spellsShown = new List<string>();

        /// <summary>Характеристики, которыми набран список сейчас — по той же причине, что и заклинания.</summary>
        private readonly List<InfoRow> _statsShown = new List<InfoRow>();

        /// <summary>Особенности, которыми набран список сейчас — по той же причине, что и заклинания.</summary>
        private readonly List<InfoRow> _traitsShown = new List<InfoRow>();

        /// <summary>Особенности, которыми набрана сетка значков сейчас — по той же причине.</summary>
        private readonly List<InfoRow> _traitIconsShown = new List<InfoRow>();

        /// <summary>Добыча, которой набрана сетка сейчас — по той же причине, что и заклинания.</summary>
        private readonly List<string> _lootShown = new List<string>();

        /// <summary>
        /// Окно открыто про своего персонажа (иначе — про выбранную цель). Задаётся кнопкой, которой окно
        /// открыли, и держится до закрытия: иначе окно о себе схлопывалось бы при выборе цели.
        /// </summary>
        private bool _aboutSelf;

        protected override void Awake()
        {
            base.Awake();

            if (infoGroup == null)
            {
                Error("не указана CanvasGroup окна сведений о цели");
                return;
            }

            if (infoButton == null)
            {
                Error("не указана кнопка открытия окна сведений о цели");
                return;
            }

            if (infoPortrait == null)
            {
                Error("не указан портрет цели в окне сведений");
                return;
            }

            if (infoSelfButton == null)
            {
                Error("не указана кнопка открытия окна сведений о своём персонаже");
                return;
            }

            if (infoName == null || infoDescription == null || infoDetails == null)
            {
                Error("не указаны текстовые поля окна сведений о цели");
                return;
            }

            if (infoStatsTitle == null || infoStatsArea == null
                || infoTraitsTitle == null || infoTraitsArea == null || infoTraitsIconArea == null)
            {
                Error("не указаны блоки характеристик и особенностей окна сведений о цели");
                return;
            }

            if (infoSpellsTitle == null || infoSpellsArea == null)
            {
                Error("не указаны заголовок и сетка заклинаний окна сведений о цели");
                return;
            }

            if (infoLootTitle == null || infoLootArea == null)
            {
                Error("не указаны заголовок и сетка добычи окна сведений о цели");
                return;
            }
        }

        /// <summary>
        /// Открыть либо закрыть окно про выбранную цель. Публичный — зовётся кнопкой у рамки цели.
        /// </summary>
        public void OpenCloseTargetInfo()
        {
            _aboutSelf = false;
            OpenClose(infoGroup);
        }

        /// <summary>
        /// Открыть либо закрыть окно про своего персонажа. Публичный — зовётся кнопкой у рамки игрока.
        /// </summary>
        public void OpenCloseSelfInfo()
        {
            _aboutSelf = true;
            OpenClose(infoGroup);
        }

        protected override void Update()
        {
            base.Update();

            if (infoGroup == null)
                return;

            // Иконка живёт вместе с рамкой цели: без выбранной цели рассказывать не о ком.
            infoButton.gameObject.SetActive(Target != null);

            if (infoGroup.alpha == 0)
            {
                // Закрытое окно портрет не ведёт: зеркало анимации стоит кадровой работы, а показать его
                // некому.
                if (_shown != null)
                {
                    _shown = null;
                    infoPortrait.Target = null;
                }

                return;
            }

            ObjectModel subject = _aboutSelf ? PlayerController.Player : Target;

            if (subject == null)
            {
                OpenClose(infoGroup);
                return;
            }

            // Наполняем каждый кадр, пока окно открыто: данные приезжают отдельными пакетами и после
            // открытия — по одному лишь событию открытия окно осталось бы с неполными сведениями.
            Fill(subject);
        }

        private void Fill(ObjectModel target)
        {
            _shown = target;
            infoPortrait.Target = target;

            infoName.text = target.DisplayName;

            PlayerModel player = target as PlayerModel;

            // Описание префаба рассказывает о ВИДЕ существа — про игрока оно сказало бы лишь «игрок такой-то
            // игры», а это и так видно. Игроку взамен показываем то, что относится к нему самому: учётную запись.
            // Пустую строку описания гасим целиком: в потоке блоков она заняла бы место наравне с заполненной.
            // Курсивом — это рассказ о виде, а не сведения об этой особи, и начертание отделяет его от строк ниже.
            string description = player == null ? (AnimationCacheService.GetPrefabDescription(target.prefab) ?? "") : "";
            infoDescription.text = description.Length > 0 ? TextStyle.Hint(description) : "";
            infoDescription.gameObject.SetActive(description.Length > 0);

            FillStats(target);
            FillTraits(target);
            FillSpells(target);
            FillLoot(target);

            string details = "";

            if (player != null)
            {
                if (!string.IsNullOrEmpty(player.ip))
                    details += "Адрес: " + player.ip + "\n";

                details += Line("Зарегистрирован", target.created);
                details += Line("Последний вход", target.updated);
            }
            else
            {
                details += Line("Создан", target.created);
                details += Line("Изменён", target.updated);
            }

            infoDetails.text = details.TrimEnd('\n');
        }

        /// <summary>
        /// Строки блока по его перечню: значение каждой разрешается общим правилом, взятое у вида идёт
        /// приглушённым. <paramref name="allTypical"/> — о виде весь блок, тогда пометка уйдёт в его
        /// заголовок, а не повторится в каждой строке. Пустой список — рассказать нечего.
        ///
        /// Картинка компонента заменяет собой ту часть строки, которая называет свойство: у строки с
        /// подписью — саму подпись (рядом остаётся значение), у строки-фразы — фразу целиком. Заменённое
        /// уходит в подсказку значка, и без неё картинка ничего бы не сказала. Взять её негде лишь у
        /// команды: компонента у неё нет, и такая строка всегда остаётся текстовой. Берём ИМЯ ФАЙЛА, а не
        /// сам спрайт: перечень перебирается каждый кадр, чтение картинки же — работа для смены набора,
        /// и делает её сам пункт списка.
        /// </summary>
        private static List<InfoRow> Collect(InfoLine[] lines, ObjectModel subject, out bool allTypical)
        {
            List<InfoRow> shown = new List<InfoRow>();
            allTypical = true;

            foreach (InfoLine line in lines)
            {
                float value;
                float? pair;
                bool typical;

                if (!Resolve(subject, line, out value, out pair, out typical))
                    continue;

                string text = line.Format(value, pair);

                // Значение есть, а показывать его нечем (запас, которого у существа не бывает) — строку
                // выбрасывает сама форма показа, и это её право: перечню о таких тонкостях знать незачем.
                if (string.IsNullOrEmpty(text))
                    continue;

                // Значок строки: свой компонент либо названный ею чужой (запас у строки восстановления).
                // У строки команды без такого имени значка не бывает — рассказывать о себе ей нечем, кроме
                // самой фразы.
                string iconKey = line.IconKey ?? (line.Source == InfoSource.Component ? line.Key : null);
                string icon = iconKey != null && ComponentCacheService.GetImage(iconKey) != null ? iconKey : null;
                // Значок заимствован — строка про прибыль этого запаса, и на значке встаёт стрелка роста:
                // сердце без неё говорило бы «здоровье», а не «здоровье прибывает».
                bool gain = line.IconKey != null;

                string full = line.Title != null ? line.Title + ": " + text : text;

                if (typical)
                {
                    full = TextStyle.Muted(full);
                    text = TextStyle.Muted(text);
                }
                else
                    allTypical = false;

                // Части подсказки готовим здесь, склеивает их сам пункт: что именно он не показал —
                // ведомо только ему, а собирать текст на месте нечем (правило сборки живёт тут).
                // Подпись зашита в клиент и называет свойство одним словом; смысл живёт у самого элемента
                // игры и правится в админке, потому под подписью идёт его описание.
                // Описание берём лишь у строки про САМ компонент: у строки восстановления значок
                // заимствован, и описание запаса объясняло бы не её.
                string caption = icon != null && line.Title != null ? TextStyle.Title(line.Title) : null;
                string about = icon != null && line.Source == InfoSource.Component
                    ? ComponentCacheService.GetDescription(line.Key) : null;

                shown.Add(new InfoRow(full, text, icon, caption,
                    string.IsNullOrEmpty(about) ? null : TextStyle.Hint(about), gain));
            }

            return shown;
        }

        /// <summary>
        /// Характеристики: заголовок и строки под ним. Одним текстом их больше не набрать — у строки,
        /// чей компонент несёт иконку, картинка встаёт вместо подписи, а картинку в надпись не вложить.
        /// </summary>
        private void FillStats(ObjectModel subject)
        {
            bool allTypical;
            List<InfoRow> shown = Collect(STATS, subject, out allTypical);

            Title(infoStatsTitle, "Характеристики", allTypical, shown.Count > 0);
            FillArea(infoStatsArea, infoStatPrefab, "Характеристики", shown, _statsShown);
        }

        /// <summary>
        /// Особенности: сперва значки в ряд, под ними строки. Значок достаётся той особенности, чей
        /// компонент несёт картинку, и рассказ о ней тогда читается подсказкой; прочим — команде,
        /// торговле, компоненту без картинки — сказать о себе нечем, кроме самой фразы, и они остаются
        /// строками. Пункт бывает длиннее строки, и его продолжение обязано вставать под текстом, а не
        /// под значком, — иначе перенос читается как начало следующего пункта.
        /// </summary>
        private void FillTraits(ObjectModel subject)
        {
            bool allTypical;
            List<InfoRow> shown = Collect(TRAITS, subject, out allTypical);

            // Торговля стоит особняком от прочих особенностей: её значение — пара множителей, а не число,
            // и перечнем строк её не описать. Собираем отдельно и дописываем в тот же список — для игрока
            // это такое же свойство существа, как бегство или зов сородичей.
            string trade = TradeLine(subject);

            if (trade != null)
                shown.Add(new InfoRow(trade));

            List<InfoRow> icons = new List<InfoRow>();
            List<InfoRow> texts = new List<InfoRow>();

            foreach (InfoRow row in shown)
                (row.Icon != null ? icons : texts).Add(row);

            Title(infoTraitsTitle, "Особенности", allTypical, shown.Count > 0);
            FillArea(infoTraitsIconArea, infoTraitIconPrefab, "Особенности", icons, _traitIconsShown);
            FillArea(infoTraitsArea, infoTraitPrefab, "Особенности", texts, _traitsShown);
        }

        /// <summary>
        /// Заголовок блока. Рассказать нечего — гаснет: пустой заголовок читался бы как «у этого существа
        /// ничего такого нет», хотя мы попросту ничего о нём не знаем. Ведёт его не сама область, потому
        /// что областей у блока бывает две (значки и строки), а заголовок над ними один.
        /// </summary>
        private static void Title(Text titleField, string title, bool allTypical, bool any)
        {
            titleField.gameObject.SetActive(any);

            if (any)
                titleField.text = Head(title, allTypical);
        }

        /// <summary>
        /// Область пунктов: гаснет вместе с пустым набором. Пункты — объекты, поэтому пересобираем их,
        /// только когда набор действительно сменился: окно наполняется каждый кадр.
        /// </summary>
        private void FillArea(RectTransform area, InfoPoint prefab, string title,
            List<InfoRow> shown, List<InfoRow> already)
        {
            if (shown.Count == 0)
            {
                area.gameObject.SetActive(false);
                already.Clear();
                return;
            }

            area.gameObject.SetActive(true);

            if (Same(already, shown))
                return;

            if (prefab == null)
            {
                Error("не указан префаб пункта блока «" + title + "» в окне сведений о цели");
                return;
            }

            // Число пунктов совпало — переписываем сами пункты: у существа изменилось значение, а не
            // набор строк, и пересобирать объекты незачем.
            if (already.Count == shown.Count)
            {
                int i = 0;
                foreach (Transform point in area)
                {
                    InfoPoint line = point.GetComponent<InfoPoint>();
                    if (line != null && i < shown.Count)
                        line.SetRow(shown[i++], tooltip);
                }
            }
            else
            {
                foreach (Transform point in area)
                    Destroy(point.gameObject);

                foreach (InfoRow row in shown)
                    Instantiate(prefab, area).SetRow(row, tooltip);
            }

            already.Clear();
            already.AddRange(shown);
        }

        /// <summary>
        /// Заклинания существа сеткой иконок; о каждом рассказывает подсказка по наведению. Состав у
        /// своего персонажа фактический (книга приходит ему пакетом), у чужой цели — типовой для её вида:
        /// чужую книгу сервер не рассылает.
        /// </summary>
        private void FillSpells(ObjectModel subject)
        {
            bool typical;
            List<string> spells = SpellsOf(subject, out typical);

            if (spells.Count == 0)
            {
                infoSpellsTitle.gameObject.SetActive(false);
                infoSpellsArea.gameObject.SetActive(false);
                _spellsShown.Clear();
                return;
            }

            infoSpellsTitle.gameObject.SetActive(true);
            infoSpellsArea.gameObject.SetActive(true);
            infoSpellsTitle.text = Head("Заклинания", typical);

            if (Same(_spellsShown, spells))
                return;

            if (infoSpellPrefab == null)
            {
                Error("не указан префаб иконки заклинания в окне сведений о цели");
                return;
            }

            foreach (Transform icon in infoSpellsArea)
                Destroy(icon.gameObject);

            foreach (string spell in spells)
                Instantiate(infoSpellPrefab, infoSpellsArea).SetData(spell, tooltip);

            _spellsShown.Clear();
            _spellsShown.AddRange(spells);
        }

        /// <summary>
        /// Добыча сеткой иконок: что с этой цели выпадает и с каким шансом. У существа розыгрыш решает
        /// смерть, у контейнера — срок пополнения, а стопроцентная строка значит «лежит наверняка». Это
        /// свойство вида из каталога, потому в бою и у сундука показано одним блоком: игрока занимает один
        /// вопрос — что отсюда достанется. Подробности каждой строки — в подсказке.
        /// </summary>
        private void FillLoot(ObjectModel subject)
        {
            List<LootRow> loot = LootOf(subject);

            if (loot.Count == 0)
            {
                infoLootTitle.gameObject.SetActive(false);
                infoLootArea.gameObject.SetActive(false);
                _lootShown.Clear();
                return;
            }

            infoLootTitle.gameObject.SetActive(true);
            infoLootArea.gameObject.SetActive(true);

            // Заголовок один на оба случая: игрока занимает, что отсюда достанется, а разыгрывается это
            // или лежит заданным — видно по самим строкам. Пометка «как у всех такого вида» тут не нужна:
            // добыча и есть свойство вида, у этой особи своей не бывает — с существа что выпадет, решится
            // в момент смерти, а состав контейнера сервер приводит к заданному его виду.
            infoLootTitle.text = Head("Добыча", false);

            List<string> shown = new List<string>();
            foreach (LootRow row in loot)
                shown.Add(row.Prefab);

            if (Same(_lootShown, shown))
                return;

            if (infoLootPrefab == null)
            {
                Error("не указан префаб иконки добычи в окне сведений о цели");
                return;
            }

            foreach (Transform icon in infoLootArea)
                Destroy(icon.gameObject);

            foreach (LootRow row in loot)
            {
                InfoLoot icon = Instantiate(infoLootPrefab, infoLootArea);
                icon.SetLoot(row.Prefab, tooltip, row.Chance, row.Min, row.Max);
            }

            _lootShown.Clear();
            _lootShown.AddRange(shown);
        }

        /// <summary>
        /// Чем торгует эта цель: во сколько раз дороже базовой цены она продаёт и во сколько дешевле
        /// скупает. Множители — свойство вида из каталога, они же рассылаются живой лавке. Нулевой
        /// множитель закрывает свою сторону торговли, и говорить о ней нечего. null — не торгует вовсе.
        /// </summary>
        private static string TradeLine(ObjectModel subject)
        {
            JObject trade = AnimationCacheService.GetComponentValue(subject.prefab, COMPONENT_TRADE, null) as JObject;

            if (trade == null)
                return null;

            float buy = Number(trade, "buy");
            float sell = Number(trade, "sell");

            if (buy <= 0 && sell <= 0)
                return null;

            string text = "Торгует: ";

            if (buy > 0)
                text += "продаёт по цене ×" + Number(buy);
            if (buy > 0 && sell > 0)
                text += ", ";
            if (sell > 0)
                text += "скупает по ×" + Number(sell);

            return text + " от обычной цены предмета";
        }

        /// <summary>
        /// Строка добычи: что за предмет, с каким шансом и сколько его выпадает. Шанс единица — предмет
        /// достаётся наверняка.
        /// </summary>
        private struct LootRow
        {
            public string Prefab;
            public float Chance;
            public int Min;
            public int Max;
        }

        /// <summary>
        /// Что достанется с этой цели — таблица её вида: с каким шансом и сколько чего выпадает. У существа
        /// она разыгрывается в момент смерти, у контейнера — каждым сроком пополнения, и стопроцентная
        /// строка значит «лежит наверняка». Свойство это видовое: чем цель богата СЕЙЧАС, сервер до поры не
        /// рассказывает, а вид известен заранее.
        /// </summary>
        private static List<LootRow> LootOf(ObjectModel subject)
        {
            List<LootRow> rows = new List<LootRow>();

            JObject table = AnimationCacheService.GetComponentValue(subject.prefab, EnemyModel.COMPONENT_LOOT_TABLE, null) as JObject;

            if (table == null)
                return rows;

            foreach (KeyValuePair<string, JToken> item in table)
            {
                JObject roll = item.Value as JObject;

                if (roll == null)
                    continue;

                LootRow row = new LootRow();
                row.Prefab = item.Key;
                // Шанс сервер держит в сотых долях процента: 10000 — всегда, 800 — восемь процентов.
                row.Chance = Number(roll, "chance") / 10000f;
                row.Min = Mathf.RoundToInt(Number(roll, "min"));
                row.Max = Mathf.RoundToInt(Number(roll, "max"));

                if (row.Chance > 0)
                    rows.Add(row);
            }

            return rows;
        }

        /// <summary>Число из структуры каталога; поля нет либо оно не число — ноль.</summary>
        private static float Number(JObject source, string key)
        {
            JToken value = source[key];

            return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                ? value.Value<float>()
                : 0f;
        }

        /// <summary>
        /// Какими заклинаниями существо владеет. Книга — приватный компонент: своему игроку она приходит
        /// целиком, о чужой цели известно лишь то, что задано её виду в каталоге префабов, — такой состав
        /// и помечаем типовым. Пустая своя книга от неприсланной неотличима, поэтому и она уводит к
        /// типовому: показать заведомо неполное хуже, чем показать обычное для вида.
        /// </summary>
        private List<string> SpellsOf(ObjectModel subject, out bool typical)
        {
            typical = false;

            if (subject == PlayerController.Player && Spells != null && Spells.Count > 0)
                return new List<string>(Spells.Keys);

            typical = true;

            List<string> spells = new List<string>();
            JToken book = AnimationCacheService.GetComponentValue(subject.prefab, SpellBookController.COMPONENT_SPELL_BOOK, null);
            JObject known = book as JObject;

            if (known == null)
                return spells;

            // Своей книги у вида нет — вместо неё пришло УМОЛЧАНИЕ компонента, а это справочник стихий,
            // общий для всей игры (см. SpellBookController.COMPONENT_SPELL_BOOK), не перечень заклинаний.
            // Читать его книгой нельзя: о заклинаниях вида мы попросту ничего не знаем.
            if (known[SpellBookController.SECTION_SPELL] != null || known[SpellBookController.SECTION_ELEMENT] != null)
                return spells;

            foreach (KeyValuePair<string, JToken> spell in known)
                if (spell.Value != null && spell.Value.Type != JTokenType.Null && spell.Value.Value<bool>())
                    spells.Add(spell.Key);

            return spells;
        }

        /// <summary>
        /// Оба числа строки для этого существа — единственная точка правила «сперва своё, потом видовое».
        /// Своё — то, что сервер прислал про НЕГО; нет своего — типовое из каталога префабов, и тогда
        /// <paramref name="typical"/> взводится: игроку показывают, что это свойство вида, а не этой
        /// особи. У команды видового значения не бывает: пока сервер не назвал её срок для этого
        /// существа, показывать нечего.
        /// Возвращает false, когда основного значения нет: строку тогда не показываем. Второго числа
        /// может не быть и при показанной строке — <paramref name="pair"/> тогда null.
        /// </summary>
        private static bool Resolve(ObjectModel subject, InfoLine line, out float value, out float? pair, out bool typical)
        {
            value = 0f;
            pair = null;
            typical = false;

            if (line.Source == InfoSource.Command)
            {
                Event command = subject.TryGetEvent(line.Key);

                if (command == null || command.timeout == null)
                    return false;

                value = (float)command.timeout.Value;
                pair = CommandData(subject, line.Key, line.PairKey);
                return true;
            }

            float? own = Component(subject, line.Key);

            if (own != null)
                value = own.Value;
            else
            {
                // Строка на присланном (см. InfoLine.OwnOnly): своего значения нет — рассказывать нечего.
                if (line.OwnOnly)
                    return false;

                float? shared = Shared(subject, line.Key);

                if (shared == null)
                    return false;

                value = shared.Value;
                typical = true;
            }

            if (line.Observed != null)
            {
                pair = line.Observed(subject);
                return true;
            }

            if (line.PairKey == null)
                return true;

            pair = Component(subject, line.PairKey);

            if (pair == null && !line.OwnOnly)
            {
                pair = Shared(subject, line.PairKey);

                // Пара разрешилась по виду — вся строка о виде: показывать половину точной, половину
                // видовой значило бы выдать «10 / 15» за сведения об этом существе.
                if (pair != null)
                    typical = true;
            }

            return true;
        }

        /// <summary>Значение, присланное сервером про это существо. null — про него такого не приходило.</summary>
        private static float? Component(ObjectModel subject, string slug)
        {
            EnemyModel creature = subject as EnemyModel;
            return creature != null ? creature.OwnComponent(slug) : null;
        }

        /// <summary>
        /// Типовое значение вида из каталога префабов. null — виду такое свойство не положено либо
        /// значения у него нет.
        /// </summary>
        private static float? Shared(ObjectModel subject, string slug)
        {
            JToken shared = AnimationCacheService.GetComponentValue(subject.prefab, slug, null);

            if (shared == null || shared.Type == JTokenType.Null)
                return null;

            if (shared.Type != JTokenType.Integer && shared.Type != JTokenType.Float)
            {
                // Свойство, которое окно показывает числом, задано в админке чем-то другим: строка
                // молча превратилась бы в ноль, а это уже неправда о существе.
                Debug.LogError("Окно сведений: значение компонента " + slug + " у префаба " + subject.prefab
                    + " не число (" + shared.Type + ") — строка пропущена");
                return null;
            }

            return shared.Value<float>();
        }

        /// <summary>
        /// Число из данных висящей на существе команды. null — команда данных не привезла: величину
        /// подставлять нечем, и сама строка обойдётся без неё (умолчание живёт на сервере, у клиента
        /// его нет — выдуманная единица соврала бы молча).
        /// </summary>
        private static float? CommandData(ObjectModel subject, string group, string key)
        {
            if (key == null)
                return null;

            Event command = subject.TryGetEvent(group);
            JObject data = command != null ? command.data : null;

            if (data == null)
                return null;

            JToken value = data[key];

            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                return null;

            return value.Value<float>();
        }

        /// <summary>
        /// Скорость, которой существо шло у нас на глазах, в клетках за секунду: длина шага, поделённая
        /// на срок между шагами. null — существо при нас не ходило, и срок шага сервер для него не
        /// называл (о чужих он его и не рассылает).
        /// </summary>
        private static float? WalkPace(ObjectModel subject)
        {
            Event walk = subject.TryGetEvent(WalkResponse.GROUP);

            if (walk == null || walk.timeout == null || walk.timeout.Value <= 0 || ConnectController.step <= 0)
                return null;

            return ConnectController.step / (float)walk.timeout.Value;
        }

        /// <summary>Заголовок блока; всё в нём видовое — пометка стоит здесь, а не в каждой строке.</summary>
        private static string Head(string title, bool typical)
        {
            return TextStyle.HEAD_GAP + TextStyle.Title(title)
                + (typical ? " " + TextStyle.Muted("— как у всех такого вида") : "");
        }

        /// <summary>Целое показываем без хвоста, дробное — с сотыми.</summary>
        private static string Number(float value)
        {
            return value.ToString("0.##");
        }

        /// <summary>
        /// Число либо запас «текущее из максимума», когда максимум известен. Максимум нулевой — запаса
        /// у существа нет вовсе (мана у зверья), и строки быть не должно: «0 / 0» читалось бы как
        /// «мана кончилась», хотя её у него и не бывает.
        /// </summary>
        private static string Amount(float value, float? max)
        {
            if (max == null)
                return Number(value);

            return max.Value > 0 ? Number(value) + " / " + Number(max.Value) : null;
        }

        /// <summary>
        /// Восстановление: как часто и по сколько за раз. Что именно прибывает, говорит сама строка —
        /// её подпись и значок запаса, — поэтому здесь только числа. Величина едет вместе с самой
        /// командой; не приехала — говорим об одной частоте. Своей единицы клиент не подставляет:
        /// сколько прибавляется, решает сервер, и выдуманное число соврало бы молча.
        /// </summary>
        private static string Recovery(float period, float? amount)
        {
            return (amount != null ? TextStyle.Gain("↑" + Number(amount.Value)) + " " : "")
                + "раз в " + Seconds(period);
        }

        /// <summary>
        /// Срок словами. От минуты и дольше считаем минутами: «раз в 120 секунд» игрок в уме и так
        /// переводит, а сроки у контейнеров и прочих долгих механик именно такие.
        /// </summary>
        private static string Seconds(float value)
        {
            if (value >= 60f)
                return Minutes(value / 60f);

            string count = Number(value);

            if (value != Mathf.Floor(value))
                return count + " секунды";

            int tail = Mathf.Abs(Mathf.RoundToInt(value)) % 100;

            if (tail >= 11 && tail <= 14)
                return count + " секунд";

            switch (tail % 10)
            {
                case 1: return count + " секунду";
                case 2:
                case 3:
                case 4: return count + " секунды";
                default: return count + " секунд";
            }
        }

        /// <summary>Минуты с окончанием по числу: 1 минуту, 2 минуты, 5 минут, 2,5 минуты.</summary>
        private static string Minutes(float value)
        {
            string count = Number(value);

            if (value != Mathf.Floor(value))
                return count + " минуты";

            int tail = Mathf.Abs(Mathf.RoundToInt(value)) % 100;

            if (tail >= 11 && tail <= 14)
                return count + " минут";

            switch (tail % 10)
            {
                case 1: return count + " минуту";
                case 2:
                case 3:
                case 4: return count + " минуты";
                default: return count + " минут";
            }
        }

        /// <summary>Доля от целого — процентами: «четверть здоровья» игроку понятнее как «25%».</summary>
        private static string Share(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }

        /// <summary>Расстояние клетками, с окончанием по числу: 1 клетка, 2 клетки, 5 клеток, 2,5 клетки.</summary>
        private static string Cells(float value)
        {
            string count = Number(value);

            if (value != Mathf.Floor(value))
                return count + " клетки";

            int tail = Mathf.Abs(Mathf.RoundToInt(value)) % 100;

            if (tail >= 11 && tail <= 14)
                return count + " клеток";

            switch (tail % 10)
            {
                case 1: return count + " клетка";
                case 2:
                case 3:
                case 4: return count + " клетки";
                default: return count + " клеток";
            }
        }

        /// <summary>
        /// Совпадают ли составы: объекты списков и сеток пересобираем только при смене набора. Один на
        /// оба вида набора — сетки помнят свой состав именами префабов, списки блоков — готовыми
        /// строками, а правило сравнения у них общее.
        /// </summary>
        private static bool Same<T>(List<T> shown, List<T> wanted)
        {
            if (shown.Count != wanted.Count)
                return false;

            for (int i = 0; i < shown.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(shown[i], wanted[i]))
                    return false;

            return true;
        }

        private static string Line(string title, string moment)
        {
            string value = Moment(moment);
            return value == null ? "" : title + ": " + value + "\n";
        }

        /// <summary>
        /// Отметка времени сервера (ISO-8601, часовой пояс сервера) в местное время игрока. Разобрать не
        /// удалось — показываем как пришло: строка сервера читаема и сама по себе, а прятать её значит
        /// скрыть от игрока сведения вовсе.
        /// </summary>
        private static string Moment(string iso)
        {
            if (string.IsNullOrEmpty(iso))
                return null;

            DateTime moment;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out moment))
                return iso;

            return moment.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }
    }
}
