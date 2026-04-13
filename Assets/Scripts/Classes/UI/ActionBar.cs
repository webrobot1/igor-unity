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

        private Image _image;

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
                    _image.sprite = null;
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
                    _image.sprite = _item.Image.sprite;
                    _image.color = _item.Image.color;
                    _image.raycastTarget = _item.Image.raycastTarget;

                    // Cooldown overlay + mana cost
                    if (_cooldownOverlay != null)
                    {
                        var (fill, remain) = _item.GetCooldownProgress();
                        if (fill > 0)
                        {
                            _cooldownOverlay.fillAmount = fill;
                            _cooldownOverlay.enabled = true;
                            _image.raycastTarget = false;

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
                _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, 0f);
                _image.raycastTarget = true;

                if (_cooldownOverlay != null)
                    _cooldownOverlay.enabled = false;
                if (_cooldownText != null)
                    _cooldownText.text = "";
            }
        }

        protected void Awake()
        {
            _image = GetComponent<Image>();
            if (_image == null)
                ConnectController.Error("не указан gameObject для быстрох клавиш отвечающий за отображение картинок");
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