using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    // Забор добычи из контейнера key (труп): своего инвентаря команда не касается, key обязателен.
    // Серверная группа — ui/inventory: сервер исполняет операции игрока с предметами по одной за тик,
    // и забор обязан стоять в той же очереди, что пересейв своего инвентаря (InventoryResponse), иначе
    // они затирали бы друг друга.
    // Отдельный класс от LootPutResponse из-за ТИПА позиции: у take параметр idx — список (сервер
    // обходит его циклом), у put — скаляр (сервер приводит к int), одним полем оба не выразить.
    // Список — единственная форма забора нескольких предметов: пачка отдельных take затёрла бы сама
    // себя (одна команда за тик). Отсюда «забрать всё» — одна команда со всеми занятыми позициями.
    // to (желаемый свой слот) сервер учитывает, только когда в списке ровно одна позиция.
    public class LootTakeResponse : Response
    {
        public string? key = null;

        public List<int>? idx = null;

        public int? to = null;

        public LootTakeResponse()
        {
            action = "take";
        }

        public override string group
        {
            get { return "ui/inventory"; }
        }
    }
}
