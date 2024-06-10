using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class ActionBar : MonoBehaviour, IPointerClickHandler
    {
        public int num;
        private MoveableObject _item;

        private Image _image;

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
                }
            }
            else
            {
                _image.color = new Color(_image.color.r, _image.color.g, _image.color.b, 0f);
                _image.raycastTarget = true;
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
                _item.Use();
            } 
        }
    }
}