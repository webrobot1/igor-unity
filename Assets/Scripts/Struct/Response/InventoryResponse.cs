using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    // Команды группы ui/inventory (единая инвентарная механика сервера):
    //   action=index (default) — полный пересейв инвентаря (inventory), своего либо контейнера key;
    //   action=take — забрать позицию idx контейнера key (to — целевой слот СВОЕГО инвентаря);
    //   action=put  — положить свою позицию idx в контейнер key (to — целевой слот контейнера).
    // Сервер сериализует операции гейтом «группа занята» — одна инвентарная операция за тик.
    // null-поля не сериализуются (NullValueHandling.Ignore в ConnectController.Send): у take/put
    // нет параметра inventory, у index — idx/to; лишний параметр сервер режет с дисконнектом.
    public class InventoryResponse : Response
    {
        // снимок слотов для index; null у take/put (поле не уйдёт в пакет)
        public Dictionary<int, InventorySlotRecive?>? inventory = null;

        // key контейнера (труп/сундук): операция над ЕГО инвентарём вместо своего.
        // null (свой инвентарь) не сериализуется — сервер применит default параметра события.
        public string? key = null;

        public int? idx = null;
        public int? to = null;

        public override string group
        {
            get { return "ui/inventory"; }
        }
    }
}
