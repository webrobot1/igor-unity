using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class ActionBar : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public int num;
        private MoveableObject _item;
        private Tooltip _tooltip;

        // Видимая иконка слота. Мирорит _item.Icon.sprite/color/preserveAspect/localScale.
        // Раньше в ActionBar был ещё корневой _image (=GetComponent<Image>()) для рендера
        // иконки/фона — после Icon-pattern (см. TASK_ui_icon_size.md) он не нужен и убран,
        // вместе с Image-компонентом на GameObject "Image" prefab'а. Raycast обрабатывает
        // ActionButton.Button (родительский GO), фон рисуется тем же ActionButton.Image (рамка слота).
        [SerializeField] private Image _icon;
        [SerializeField] private Image _cooldownOverlay;
        [SerializeField] private Text _cooldownText;

        // Пустой слот бьёт ближней атакой. Как это выглядит — забота фоновой плашки кнопки (у таких
        // слотов на ней нарисован меч), потому тут только само поведение. Выключено (обычные слоты
        // панели) — пустой слот бездействует. Положенный предмет либо заклинание перекрывает и
        // картинку, и действие: содержимое слота хранит сервер, оно главнее.
        [SerializeField] private bool _meleeOnEmpty;

        public MoveableObject Item
        {
            get
            {
                return _item??null;
            }
            set
            {
                _item = value;
                if (value == null)
                {
                    // _image.sprite/color НЕ трогаем — это статичная фоновая плашка из prefab'а
                    if (_icon != null)
                    {
                        _icon.sprite = null;
                        _icon.enabled = false; // иначе sprite=null → Unity рисует default white quad и затирает плашку
                        _icon.transform.localScale = Vector3.one;
                    }
                }
                else
                {
                    FixedUpdate();
                }
            }
        }

        protected void FixedUpdate()
        {
            if (_item!=null)
            {
                if (PlayerController.Player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE)
                {
                    // Мирорим видимую иконку с server-size scale (1/size). Затемнение недоступного
                    // считаем сами: карточка книги гасит свою иконку только пока открыта её вкладка,
                    // и её цвет замер бы на последнем значении перед скрытием.
                    if (_icon != null && _item.Icon != null)
                    {
                        Color color = _item.Icon.color;
                        color.a = _item.IsUnavailable() ? 0.5f : 1f;

                        _icon.sprite = _item.Icon.sprite;
                        _icon.enabled = _icon.sprite != null;
                        _icon.color = color;
                        _icon.preserveAspect = _item.Icon.preserveAspect;
                        _icon.transform.localScale = _item.Icon.transform.localScale;
                    }

                    // Cooldown overlay + mana cost
                    if (_cooldownOverlay != null)
                    {
                        var (fill, remain) = _item.GetCooldownProgress();
                        if (fill > 0)
                        {
                            _cooldownOverlay.fillAmount = fill;
                            _cooldownOverlay.enabled = true;
                            if (_cooldownText != null)
                            {
                                _cooldownText.text = remain.ToString("F2");
                                _cooldownText.color = Color.white;
                            }
                        }
                        else
                        {
                            _cooldownOverlay.fillAmount = 0;
                            _cooldownOverlay.enabled = false;

                            if (_cooldownText != null)
                            {
                                int manaCost = _item.ManaCost;
                                if (manaCost > 0 && PlayerController.Player.mp < manaCost)
                                {
                                    _cooldownText.text = manaCost.ToString();
                                    _cooldownText.color = new Color(0.3f, 0.5f, 1f);
                                }
                                else
                                {
                                    _cooldownText.text = "";
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (_icon != null)
                {
                    _icon.sprite = null;
                    _icon.enabled = false;
                    _icon.transform.localScale = Vector3.one;
                }

                // Откат ближней атаки — тот же таймаут группы событий, что у заклинаний, и показываем
                // его так же: заливка поверх плашки плюс остаток секундами.
                double meleeRemain = MeleeRemain();
                if (meleeRemain > 0)
                {
                    if (_cooldownOverlay != null)
                    {
                        double timeout = PlayerController.Player.EventTimeout(MeleeResponse.GROUP);
                        _cooldownOverlay.fillAmount = timeout > 0 ? (float)(meleeRemain / timeout) : 0f;
                        _cooldownOverlay.enabled = true;
                    }
                    if (_cooldownText != null)
                    {
                        _cooldownText.text = meleeRemain.ToString("F2");
                        _cooldownText.color = Color.white;
                    }
                }
                else
                {
                    if (_cooldownOverlay != null)
                        _cooldownOverlay.enabled = false;
                    if (_cooldownText != null)
                        _cooldownText.text = "";
                }
            }
        }

        /// <summary>Сколько секунд осталось до следующей ближней атаки. 0 — можно бить.</summary>
        private double MeleeRemain()
        {
            if (!_meleeOnEmpty || PlayerController.Player == null)
                return 0;

            return PlayerController.Player.GetEventRemain(MeleeResponse.GROUP);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("Быстрая клавиша " + num + ": нажали "+(_item == null?"на пустую":"на присвоенную"));

            // на сервере есть првоерка на то можем ли мы стрелять, но что бы не сдать впустую запрос который никчему не приведет  - ограничим и тут
            if (PlayerController.Player == null || PlayerController.Player.action == PlayerController.ACTION_REMOVE || PlayerController.Player.hp <= 0)
                return;

            if (_item != null)
            {
                if (_item.IsOnCooldown())
                    return;

                _item.Use();
            }
            else if (_meleeOnEmpty)
            {
                if (MeleeRemain() > 0)
                    return;

                SendMelee();
            }
        }

        // Ближняя атака с пустого слота. Цель берём ту же, что заклинания (MainController.Instance.Target),
        // и по тем же правилам: живой цели шлём её ключ — сервер сам подойдёт и ударит; без цели бьём
        // по направлению взгляда (fight/melee принимает вектор forward вместо ключа).
        // Мёртвую цель не шлём: сервер такую команду молча отбрасывает — бьём по направлению. Не шлём и
        // цель без запаса здоровья вовсе (сундук, лавка, портал, лежащая вещь): выбрать их можно —
        // окно сведений рассказывает и о них, — но бить там некого, и сервер такую команду тоже отбивает.
        private void SendMelee()
        {
            MeleeResponse response = new MeleeResponse();
            ObjectModel target = MainController.Instance.Target;
            EnemyModel enemy = target as EnemyModel;

            if (enemy != null && enemy.hp > 0)
                response.target = target.key;
            else
            {
                response.x = System.Math.Round(PlayerController.Player.Forward.x, PlayerController.position_precision);
                response.y = System.Math.Round(PlayerController.Player.Forward.y, PlayerController.position_precision);
            }

            response.Send();
        }

        public void SetTooltip(Tooltip tooltip)
        {
            _tooltip = tooltip;
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            if (_item != null && _tooltip != null)
            {
                string text = _item.GetTooltipText();
                if (text != null)
                    _tooltip.Show((RectTransform)transform, text);
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }
    }
}