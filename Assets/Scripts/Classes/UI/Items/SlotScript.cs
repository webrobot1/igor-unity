using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
    public class SlotScript : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, ITakeable
    {
        // protected — EquipmentSlot переиспользует тот же binding из Inspector
        // (свой _icon-биндинг в prefab'е equipment-слота уже привязан к этому полю).
        [SerializeField] protected Image _icon;
        [SerializeField] private Text _stackText;

        // Тултип показывает СЛОТ, а не Item: Item в слоте деактивирован (SetActive(false)
        // в InventoryController.RenderSlotItem) и pointer-события не получает.
        protected Tooltip tooltip;

        private Item _item;
        private int _slotNum;

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

        // count=1 по умолчанию — для экипировки на теле всегда «один экземпляр», stack-текст
        // не показывается (count > 1 == false). В инвентаре вызываем с явным item.Count из сервера.
        // Отличия экземпляра (Item.Components) ячейка не хранит: они едут с самим предметом.
        public void SetItem(Item item, int count = 1)
        {
            _item = item;

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
            _icon.sprite = null;
            _icon.color = new Color(1, 1, 1, 0);
            _stackText.text = "";
        }

        // Отвязать слот от Item, НЕ уничтожая Item.gameObject. Нужно при локальном swap'е:
        // Item переезжает в другой слот, старый Clear() в этой ситуации бы Destroy'ил _item.gameObject
        // (т.к. _item ещё указывает на тот же Item), и новый слот оставался бы с ссылкой на destroyed-object —
        // клики бы не работали до прихода ответа от сервера (см. HandlePointerClick — там _item == null
        // для destroyed Unity object возвращает true и TakeMoveable не вызывается).
        public void Detach()
        {
            _item = null;
            _icon.sprite = null;
            _icon.color = new Color(1, 1, 1, 0);
            _stackText.text = "";
        }

        public void SetTooltip(Tooltip t)
        {
            tooltip = t;
        }

        /// <summary>
        /// Возьмёт ли нажатие предмет ячейки на курсор. Слот чужого контейнера отдаёт лишь то, что нам
        /// отдадут: чужую добычу до истечения срока и не по карману товар лавки сервер отбивает молча —
        /// без этого предмет висел бы на курсоре, а команда потом никуда не уходила. Тем же признаком
        /// указатель принимает форму руки, поэтому правило одно на нажатие и на курсор.
        /// </summary>
        public bool CanTake
        {
            get
            {
                if (CursorController.MyMoveable != null || Item == null)
                    return false;

                LootSlotMarker loot = GetComponent<LootSlotMarker>();

                return loot == null || LootWindowController.CanTakeSlot(loot.Num);
            }
        }

        /// <summary>
        /// Погасить ячейку недоступного предмета: сделка по нему не состоится (не по карману, торговец
        /// его не продаёт), и сервер такую команду отбивает молча. Ячейка остаётся видимой и с подсказкой —
        /// причину называет она, гашение лишь показывает, что клик тут ничего не даст.
        /// Ставится ПОСЛЕ SetItem: тот возвращает иконке полный цвет на каждой перерисовке.
        /// </summary>
        public void SetDimmed(bool dimmed)
        {
            if (_item == null || _icon == null || _icon.sprite == null)
                return;

            _icon.color = dimmed ? new Color(0.4f, 0.4f, 0.4f, 0.7f) : Color.white;
        }

        // Общий каркас сетки слотов для инвентаря игрока (InventoryController) и окна контейнера
        // (LootWindowController): чистит area, создаёт count слотов из prefab, вешает tooltip.
        // Различия (номер слота / метка контейнера LootSlotMarker) наследник задаёт через configure.
        // Оба контроллера держат свою static-ссылку на возвращённый массив — их два (свой инвентарь
        // и контейнер) на одном MainController-инстансе, поэтому массив не в общем поле, а тут — фабрика.
        public static SlotScript[] BuildGrid(GameObject prefab, Transform area, int count, string namePrefix, Tooltip tooltip, System.Action<SlotScript, int> configure)
        {
            foreach (Transform child in area)
                Destroy(child.gameObject);

            SlotScript[] slots = new SlotScript[count];
            for (int i = 0; i < count; i++)
            {
                GameObject obj = Instantiate(prefab, area);
                obj.name = namePrefix + (i + 1);
                SlotScript slot = obj.GetComponent<SlotScript>();
                slot.SetTooltip(tooltip);
                configure?.Invoke(slot, i);
                slots[i] = slot;
            }
            return slots;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            HandlePointerClick(eventData);
        }

        // Через property Item, не поле _item: EquipmentSlot переопределяет Item на чтение
        // из инвентаря (ярлык-pattern), собственного _item у него нет.
        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            if (Item != null && tooltip != null)
            {
                string text = Item.GetTooltipText();
                if (text != null)
                    tooltip.Show((RectTransform)transform, text);
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            if (tooltip != null)
                tooltip.Hide();
        }

        // Точка переопределения для наследников (EquipmentSlot). Базовое поведение —
        // взять предмет в курсор, если он не пуст и курсор пуст. Override может полностью
        // изменить логику (например, сразу отправить equip-запрос для EquipmentSlot).
        protected virtual void HandlePointerClick(PointerEventData eventData)
        {
            if (!CanTake)
                return;

            // Метка ячейки чужого контейнера: по ней сделка идёт в контейнер, а не по инвентарю.
            LootSlotMarker loot = GetComponent<LootSlotMarker>();

            // Правая кнопка — сама сделка, без переноса через курсор: у ячейки контейнера это забрать
            // (у лавки — купить), у своей ячейки при открытом контейнере — положить (продать).
            // Перетаскивание остаётся вторым путём — им игрок выбирает КОНКРЕТНУЮ ячейку-цель, чего
            // одним нажатием не выразить. Количество, как и при переносе, спрашивает окно сделки.
            // На телефоне правой кнопки нет, и там остаётся одно перетаскивание: нужен ли третий способ
            // (тап с выделением и общей кнопкой сделки в каждом окне) — открытый вопрос,
            // Plans/trade-window-input.md.
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (loot != null)
                    LootWindowController.SendTake(new List<int> { loot.Num });
                else if (SlotNum > 0 && LootWindowController.IsOpen)
                    LootWindowController.SendPut(SlotNum);

                return;
            }

            CursorController.TakeMoveable(_item);
        }
    }
}
