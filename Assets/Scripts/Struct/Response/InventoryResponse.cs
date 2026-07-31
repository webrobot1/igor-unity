using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    // Полный пересейв СВОЕГО инвентаря (действие index серверной группы ui/inventory): раскладка после
    // перетаскивания уходит одним снимком, сервер валидирует «предметы не появились и не выросли
    // в количестве».
    // Чужой контейнер этим событием не адресуется: параметра key у него нет, а лишний параметр
    // в пакете сервер режет с дисконнектом. Перенос между инвентарём и добычей — LootTakeResponse
    // и LootPutResponse (те же ui/inventory: очередь одна на все операции игрока с предметами).
    public class InventoryResponse : Response
    {
        // снимок слотов: позиция → предмет либо null (пустая позиция)
        public Dictionary<int, ItemSlotRecive?>? inventory = null;

        public override string group
        {
            get { return "ui/inventory"; }
        }
    }
}
