using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Контроллер инвентаря — управляет слотами и предметами
    /// </summary>
    abstract public class InventoryController : SpellBookController
    {
        /// <summary>
        /// Prefab предмета-денег. Деньги в этой игре — обычный предмет инвентаря, признака «это валюта»
        /// сервер не отдаёт: у позиции есть только prefab и count. Пока такого признака нет, знание
        /// «монета — это деньги» живёт на клиенте, и держать его надо ровно здесь — единственной
        /// константой. Появится вторая валюта — заводить признак на сервере, а не второй slug рядом.
        /// </summary>
        public const string MONEY_PREFAB = "coin";

        [Header("Для работы с инвентарём")]

        [SerializeField]
        private Transform inventorySlotArea;

        [SerializeField]
        private GameObject slotPrefab;

        [SerializeField]
        private Item itemPrefab;

        [Header("Плашка денег в окне инвентаря")]

        [SerializeField]
        private Image moneyIcon;

        [SerializeField]
        private Text moneyAmount;

        // Всплывающая цифра над плашкой: прибыль и убыль показываются там, где игрок эти деньги и
        // видит. Префаб один на обе плашки — свою и торговца, поэтому ссылка живёт здесь, у владельца
        // знания о валюте (MONEY_PREFAB, Money, Coins).
        [SerializeField]
        private GameObject moneyPopupPrefab;

        private static GameObject _moneyPopupPrefab;

        // Кошелёк, по которому нарисована плашка сейчас: разницей с ним считается всплывающая цифра.
        // -1 — инвентарь ещё не приходил, показывать нечего (первый приход не прибыль).
        private static int _shownMoney = -1;

        private static SlotScript[] _slots;
        private static bool _dirty;
        private Dictionary<string, Item> _items;

        public Dictionary<string, Item> Items
        {
            get { return _items; }
            set { }
        }

        protected override void Awake()
        {
            base.Awake();

            _items = new Dictionary<string, Item>();
            _slots = null;
            _dirty = false;

            // статики чистить вручную: Enter Play Mode без Domain Reload их не сбрасывает
            _shownMoney = -1;

            if (inventorySlotArea == null)
            {
                Error("не указан Transform контейнер для слотов инвентаря");
                return;
            }

            if (slotPrefab == null)
            {
                Error("не указан префаб слота инвентаря");
                return;
            }

            if (itemPrefab == null)
            {
                Error("не указан префаб предмета");
                return;
            }

            if (!inventorySlotArea.IsChildOf(inventoryGroup.transform))
            {
                Error("контейнер слотов инвентаря не является частью CanvasGroup инвентаря");
                return;
            }

            if (moneyIcon == null)
            {
                Error("не указана иконка монеты на плашке денег инвентаря");
                return;
            }

            if (moneyAmount == null)
            {
                Error("не указан Text суммы денег инвентаря");
                return;
            }

            if (!moneyAmount.transform.IsChildOf(inventoryGroup.transform))
            {
                Error("плашка денег не является частью CanvasGroup инвентаря");
                return;
            }

            if (moneyPopupPrefab == null)
            {
                Error("не назначен префаб всплывающей цифры денег");
                return;
            }

            _moneyPopupPrefab = moneyPopupPrefab;
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<int, ItemSlotRecive> inventory = ((PlayerRecive)recive).components.inventory;

                if (inventory != null)
                {
                    if (_slots == null)
                        InitializeSlots(inventory.Count);

                    _items = new Dictionary<string, Item>();

                    foreach (var slot in inventory)
                    {
                        if (slot.Key < 1 || slot.Key > _slots.Length)
                        {
                            Error("Пришел номер слота инвентаря " + slot.Key + " однако настроено лишь " + _slots.Length);
                            return null;
                        }

                        SlotScript slotUI = _slots[slot.Key - 1];
                        slotUI.Clear();

                        if (slot.Value != null && !string.IsNullOrEmpty(slot.Value.prefab))
                        {
                            Item item = RenderSlotItem(slotUI, itemPrefab, slot.Value, slot.Key);

                            _items[slot.Value.prefab] = item;

                            player?.Log("Инвентарь слот " + slot.Key + ": " + slot.Value.prefab + " x" + slot.Value.count);
                        }
                        else
                        {
                            slotUI.Clear();
                        }
                    }

                    RefreshMoney();
                }
            }

            return base.UpdateObject(map_id, key, recive);
        }

        /// <summary>
        /// Пересчитать плашку денег. Сумма берётся по UI-слотам, а не по пришедшему словарю:
        /// слоты держат полную картину инвентаря независимо от того, какие позиции были в пакете.
        /// Отдельный запрос к серверу не нужен — количество денег уже лежит в count позиций.
        ///
        /// Точек вызова две, потому что инвентарь меняют обе стороны: сервер (добыча, подбор с земли)
        /// присылает новое состояние в UpdateObject, а перекладывание и выброс в мир клиент считает
        /// сам и на ui/inventory/index ответа-дельты не получает — там пересчёт идёт из
        /// SendFullInventory, в момент фиксации нового состояния слотов.
        ///
        /// Монет нет — показываем 0, плашку не прячем: она часть неподвижной рамки окна, и
        /// исчезновение читалось бы как поломка, тогда как «0» — определённый ответ игроку.
        ///
        /// Иконка назначается здесь, а не в Awake: картинка prefab'а лежит в кеше, который
        /// наполняется входом в игру (AnimationCacheService.SyncAll), а инвентарь приходит уже после.
        /// </summary>
        private void RefreshMoney()
        {
            // Тот же резерв, что у иконок предметов (MoveableObject.ApplyPrefabImage): без него
            // Image с пустым sprite рисует белый прямоугольник, неотличимый от поломки вёрстки.
            moneyIcon.sprite = AnimationCacheService.GetPrefabSprite(GAME_ID, MONEY_PREFAB)
                ?? Resources.Load<Sprite>("unknow");

            int money = Money;

            // Разницу показываем цифрой у самой плашки — это единственное место, где игрок видит
            // деньги. Первый приход инвентаря разницей не считается: там весь кошелёк «появился».
            if (_shownMoney >= 0 && money != _shownMoney)
                ShowMoneyPopup(moneyAmount.transform.parent as RectTransform, money - _shownMoney);

            _shownMoney = money;
            moneyAmount.text = money.ToString();
        }

        /// <summary>
        /// Всплывающая цифра изменения денег над плашкой: у своей — сколько прибыло или ушло у игрока,
        /// у плашки лавки — сколько прибавилось либо убыло у торговца. Показ идёт ИМЕННО у плашки:
        /// цифра над персонажем во время торга закрыта окном, а у плашки видна всегда, пока видно и
        /// сами деньги. Плашка скрыта (инвентарь закрыт) — вместе с ней скрыта и цифра, это норма.
        /// </summary>
        public static void ShowMoneyPopup(RectTransform panel, int delta)
        {
            if (_moneyPopupPrefab == null || panel == null || delta == 0)
                return;

            GameObject go = Instantiate(_moneyPopupPrefab, panel);
            RectTransform rt = go.transform as RectTransform;

            // Над плашкой по центру: якорь берём у самой плашки, чтобы правка её размеров в сцене
            // не тянула за собой правку кода.
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 2);
            rt.localScale = Vector3.one;

            Text text = go.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = (delta > 0 ? "+" : "-") + Mathf.Abs(delta);
                text.color = new Color(1f, 0.84f, 0.2f);
            }
        }

        /// <summary>
        /// Запас денег игрока — сумма по всем его слотам: отдельного счёта сервер не ведёт, а монеты лежат
        /// несколькими стаками. Считается по UI-слотам (полная картина инвентаря независимо от того, какие
        /// позиции были в последнем пакете) — тот же источник, что у плашки денег, второго носителя суммы
        /// нет. Инвентарь ещё не приходил — 0.
        /// </summary>
        public static int Money
        {
            get
            {
                int total = 0;
                if (_slots == null) return total;

                for (int i = 0; i < _slots.Length; i++)
                {
                    Item item = _slots[i].Item;

                    if (item != null && item.Prefab == MONEY_PREFAB)
                        total += item.Count;
                }

                return total;
            }
        }

        /// <summary>
        /// Сумма прописью для интерфейса: «1 монета», «2 монеты», «5 монет». Форма слова — здесь, у
        /// единственного места, знающего про валюту игры (см. MONEY_PREFAB).
        /// </summary>
        public static string Coins(int amount)
        {
            int tens = amount % 100;
            int ones = amount % 10;

            string word = tens >= 11 && tens <= 14 ? "монет"
                : ones == 1 ? "монета"
                : ones >= 2 && ones <= 4 ? "монеты"
                : "монет";

            return amount + " " + word;
        }

        // создает пустые ячейки инвентаря в количестве count
        private void InitializeSlots(int count)
        {
            _slots = SlotScript.BuildGrid(slotPrefab, inventorySlotArea, count, "Slot", tooltip,
                (slot, i) => slot.SlotNum = i + 1);
        }

        /// <summary>
        /// Наполнить UI-слот предметом из данных слота инвентаря (общий рендер для окна инвентаря и окна
        /// контейнера): инстанс префаба, спрайт, счётчик. Подсказку показывает сам слот (SlotScript),
        /// предмет в нём деактивирован. slotNum — номер СВОЕГО инвентаря
        /// (по нему ветвятся Item.Use/equip); 0 — предмет чужого контейнера (позицию несёт LootSlotMarker).
        /// </summary>
        protected Item RenderSlotItem(SlotScript slotUI, Item prefab, ItemSlotRecive data, int slotNum)
        {
            Item item = Instantiate(prefab, slotUI.transform);
            item.gameObject.SetActive(false);
            item.SetData(data.prefab);
            item.SlotNum = slotNum;
            item.Count = data.count;
            // Отличия экземпляра от префаба (своя цена и т.п.) — свойство самого предмета, а не ячейки.
            // Обратно на сервер уходят ровно они (SnapshotSlots), поэтому храним их как пришли.
            item.Components = data.components;
            // Единственная точка сборки: дальше предмет знает о себе всё, и окна читают его свойства
            // готовыми — правило «своё, иначе префабное» ни одно из них не повторяет.
            item.Values = AnimationCacheService.GetComponentValues(data.prefab, data.components);

            slotUI.SetItem(item, data.count);
            return item;
        }

        /// <summary>
        /// Снимок UI-слотов → словарь позиций для ui/inventory/index (null = пустая позиция).
        /// </summary>
        protected static Dictionary<int, ItemSlotRecive> SnapshotSlots(SlotScript[] slots)
        {
            Dictionary<int, ItemSlotRecive> snapshot = new Dictionary<int, ItemSlotRecive>();

            for (int i = 0; i < slots.Length; i++)
            {
                SlotScript src = slots[i];
                snapshot[i + 1] = src.Item != null
                    ? new ItemSlotRecive(src.Item.Prefab, src.Item.Count, src.Item.Components)
                    : null;
            }

            return snapshot;
        }

        /// <summary>
        /// Число слотов инвентаря (0 — инвентарь ещё не приходил). У сервера один компонент
        /// inventory на всех носителей — окно контейнера (LootWindowController) строит сетку того же размера.
        /// </summary>
        public static int SlotCount
        {
            get { return _slots != null ? _slots.Length : 0; }
        }

        /// <summary>
        /// Найти первый пустой слот (1-based). 0 если нет свободных.
        /// </summary>
        public static int FindEmptySlot()
        {
            if (_slots == null) return 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Item == null)
                    return i + 1;
            }
            return 0;
        }

        /// <summary>
        /// Получить предмет по номеру слота (1-based). Null если слот пуст или не существует.
        /// </summary>
        public static Item GetItemBySlot(int slotNum)
        {
            if (_slots == null || slotNum < 1 || slotNum > _slots.Length) return null;
            return _slots[slotNum - 1].Item;
        }

        /// <summary>
        /// Локальный swap двух слотов (без отправки на сервер).
        /// Item.gameObject НЕ уничтожаются — они просто переходят к другому слоту.
        /// (Destroy'ить нельзя: тот же Item-объект используется как ссылка из EquipmentSlot,
        /// а ответ сервера сам пересоздаст все Item'ы через UpdateObject.)
        /// </summary>
        public static void LocalSwap(int fromSlot, int toSlot)
        {
            if (_slots == null) return;

            SlotScript from = _slots[fromSlot - 1];
            SlotScript to = _slots[toSlot - 1];

            Item fromItem = from.Item;
            Item toItem = to.Item;

            _dirty = true;

            // detach обоих, чтобы Clear не сработал в Destroy и не убил Item, который мы переносим
            from.Detach();
            to.Detach();

            if (fromItem != null)
            {
                fromItem.SlotNum = toSlot;
                to.SetItem(fromItem, fromItem.Count);
            }

            if (toItem != null)
            {
                toItem.SlotNum = fromSlot;
                from.SetItem(toItem, toItem.Count);
            }
        }

        /// <summary>
        /// Отвязать слот от Item без уничтожения Item.gameObject. Нужно когда предмет уезжает в курсор
        /// (swap-with-displaced) — обычный LocalDrop в этом случае уничтожает Item ещё до того как
        /// CursorController успеет взять его в TakeMoveable.
        /// </summary>
        public static void LocalDetach(int slotNum)
        {
            if (_slots == null) return;
            _slots[slotNum - 1].Detach();
            _dirty = true;
        }

        /// <summary>
        /// Локально положить предмет в слот (из руки, без swap)
        /// </summary>
        public static void LocalPlace(int toSlot, Item item)
        {
            if (_slots == null) return;
            SlotScript slot = _slots[toSlot - 1];
            item.SlotNum = toSlot;
            slot.SetItem(item, item.Count);
            _dirty = true;
        }

        /// <summary>
        /// Локально очистить слот (без отправки на сервер)
        /// </summary>
        public static void LocalDrop(int slotNum)
        {
            if (_slots == null) return;
            _slots[slotNum - 1].Clear();
            _dirty = true;
        }

        /// <summary>
        /// Локально убрать из слота count единиц: часть стака либо весь. Остаток остаётся в слоте —
        /// сколько выбросить, игрок назвал сам (QuantityPromptController), и унести заодно остальное
        /// нельзя. count больше стака трактуем как весь стак: потолок ввода — дело спрашивающего.
        /// </summary>
        public static void LocalDropCount(int slotNum, int count)
        {
            if (_slots == null) return;

            SlotScript slot = _slots[slotNum - 1];
            Item item = slot.Item;

            if (item == null || count >= item.Count)
            {
                slot.Clear();
                _dirty = true;
                return;
            }

            item.Count -= count;
            slot.SetItem(item, item.Count);
            _dirty = true;
        }

        /// <summary>
        /// Отправить инвентарь если были локальные изменения (вызывать когда сессия перетаскивания завершена)
        /// </summary>
        public static void SendIfDirty()
        {
            if (_dirty)
                SendFullInventory();
        }

        /// <summary>
        /// Отправить полное состояние инвентаря на сервер
        /// </summary>
        public static void SendFullInventory()
        {
            if (_slots == null) return;

            InventoryResponse response = new InventoryResponse();
            response.inventory = SnapshotSlots(_slots);

            response.Send();
            _dirty = false;

            MainController.Instance.RefreshMoney();
        }
    }
}
