using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    public class InventoryResponse : Response
    {
        public Dictionary<int, InventorySlotRecive?> inventory = new Dictionary<int, InventorySlotRecive?>();

        public override string group
        {
            get { return "ui/inventory"; }
        }
    }
}
