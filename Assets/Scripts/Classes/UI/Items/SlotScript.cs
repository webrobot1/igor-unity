using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public class SlotScript : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Text _stackText;

        private Item _item;
        private int _slotNum;
        private int _count;
        private Dictionary<string, string> _components;

        public int SlotNum
        {
            get { return _slotNum; }
            set { _slotNum = value; }
        }

        public Item Item
        {
            get { return _item; }
        }

        public int Count
        {
            get { return _count; }
        }

        public Dictionary<string, string> Components
        {
            get { return _components; }
        }

        public void SetItem(Item item, int count)
        {
            _item = item;
            _count = count;

            if (item != null)
            {
                _icon.sprite = item.Image.sprite;
                _icon.color = Color.white;
                _stackText.text = count > 1 ? count.ToString() : "";
            }
            else
            {
                Clear();
            }
        }

        public void SetComponents(Dictionary<string, string> components)
        {
            _components = components;
        }

        public void Clear()
        {
            _item = null;
            _count = 0;
            _components = null;
            _icon.sprite = null;
            _icon.color = new Color(1, 1, 1, 0);
            _stackText.text = "";

        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            // Только подбор — если рука пустая и слот не пустой
            if (CursorController.MyMoveable == null && _item != null)
                CursorController.TakeMoveable(_item);
        }
    }
}
