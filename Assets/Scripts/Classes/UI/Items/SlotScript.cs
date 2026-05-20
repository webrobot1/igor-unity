using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public class SlotScript : MonoBehaviour, IPointerClickHandler
    {
        // protected — EquipmentSlot переиспользует тот же binding из Inspector
        // (свой _icon-биндинг в prefab'е equipment-слота уже привязан к этому полю).
        [SerializeField] protected Image _icon;
        [SerializeField] private Text _stackText;

        private Item _item;
        private int _slotNum;
        private Dictionary<string, string> _components;

        public int SlotNum
        {
            get { return _slotNum; }
            set { _slotNum = value; }
        }

        // virtual — EquipmentSlot переопределяет на чтение через InventoryController.GetItemBySlot,
        // не храня собственную ссылку (ярлык-pattern: source-of-truth = inventory).
        public virtual Item Item
        {
            get { return _item; }
        }

        public Dictionary<string, string> Components
        {
            get { return _components; }
        }

        // count=1 по умолчанию — для экипировки на теле всегда «один экземпляр», stack-текст
        // не показывается (count > 1 == false). В инвентаре вызываем с явным item.Count из сервера.
        public void SetItem(Item item, int count = 1, Dictionary<string, string> components = null)
        {
            _item = item;
            _components = components;

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

        // virtual — EquipmentSlot переопределяет: там Item не свой, а ссылка на inventory,
        // поэтому Destroy неуместен.
        public virtual void Clear()
        {
            if (_item != null)
                Destroy(_item.gameObject);
            _item = null;
            _components = null;
            _icon.sprite = null;
            _icon.color = new Color(1, 1, 1, 0);
            _stackText.text = "";
        }

        // Отвязать слот от Item, НЕ уничтожая Item.gameObject. Нужно при локальном swap'е:
        // Item переезжает в другой слот, старый Clear() в этой ситуации бы Destroy'ил _item.gameObject
        // (т.к. _item ещё указывает на тот же Item), и новый слот оставался бы с ссылкой на destroyed-object —
        // клики бы не работали до прихода ответа от сервера (см. Item.OnPointerClick — там _item == null
        // для destroyed Unity object возвращает true и TakeMoveable не вызывается).
        public void Detach()
        {
            _item = null;
            _components = null;
            _icon.sprite = null;
            _icon.color = new Color(1, 1, 1, 0);
            _stackText.text = "";
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            HandlePointerClick(eventData);
        }

        // Точка переопределения для наследников (EquipmentSlot). Базовое поведение —
        // взять предмет в курсор, если он не пуст и курсор пуст. Override может полностью
        // изменить логику (например, сразу отправить equip-запрос для EquipmentSlot).
        protected virtual void HandlePointerClick(PointerEventData eventData)
        {
            if (CursorController.MyMoveable == null && _item != null)
                CursorController.TakeMoveable(_item);
        }
    }
}
