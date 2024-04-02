using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyFantasy
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class ActionBar : MonoBehaviour, IPointerClickHandler
    {
        public int num;

        private MoveableObject _item;
        public MoveableObject Item
        {
            get 
            { 
                return _item??null;
            }
            set
            {
                if (value == null)
                {
                    DestroyImmediate(_icon.GetComponent<Image>());
                }
                else
                {
                    _icon.AddComponent<Image>(value.GetComponent<Image>());
                }

                _item = value;
            }
        }

        [SerializeField]
        private GameObject _icon;


        protected void Awake()
        {
            if (_icon == null)
                ConnectController.Error("не указан gameObject для быстрох клавиш отвечающий за отображение картинок");
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            Debug.LogWarning("Быстрая клавиша " + num + ": нажали "+(_item == null?"на пустую":"на присвоенную"));

            // на сервере есть првоерка на то можем ли мы стрелять, но что бы не сдать впустую запрос который никчему не приведет  - ограничим и тут
            if (_item != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
            {
                _item.Use();
            } 
        }
    }
}