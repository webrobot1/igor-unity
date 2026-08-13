using System;
using System.Globalization;
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
    /// описание его префаба из библиотеки и отметки времени с сервера. У игрока к ним добавляется адрес
    /// подключения, а сами отметки читаются иначе: заведение записи — это регистрация, последнее
    /// изменение — последний вход в игру.
    /// </summary>
    abstract public class InfoWindowController : DebugPanelController
    {
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
        /// строки об учётной записи и отметках времени
        /// </summary>
        [SerializeField]
        private Text infoDetails;

        /// <summary>
        /// чьи сведения показаны сейчас: по смене того, о ком окно, портрет пересобирается
        /// </summary>
        private ObjectModel _shown;

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
            infoDescription.text = player == null ? (AnimationCacheService.GetPrefabDescription(target.prefab) ?? "") : "";

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
