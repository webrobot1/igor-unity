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
                    // Мирорим видимую иконку с server-size scale (1/size). Cooldown-alpha идёт через _icon.color.
                    if (_icon != null && _item.Icon != null)
                    {
                        _icon.sprite = _item.Icon.sprite;
                        _icon.enabled = _icon.sprite != null;
                        _icon.color = _item.Icon.color;
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
                if (_cooldownOverlay != null)
                    _cooldownOverlay.enabled = false;
                if (_cooldownText != null)
                    _cooldownText.text = "";
            }
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("Быстрая клавиша " + num + ": нажали "+(_item == null?"на пустую":"на присвоенную"));

            // на сервере есть првоерка на то можем ли мы стрелять, но что бы не сдать впустую запрос который никчему не приведет  - ограничим и тут
            if (_item != null && PlayerController.Player!=null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
            {
                if (_item.IsOnCooldown())
                    return;

                _item.Use();
            }
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
                    _tooltip.Show(transform.position, text);
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }
    }
}