using System.Collections.Generic;

#nullable enable

namespace Mmogick
{
    // Партиальный апдейт экипировки. items: slot_slug → inventory_idx; null = снять slot.
    // Сервер каскадом обновит компоненты equip и inventory (см. components/equip.php).
    public class EquipmentResponse : Response
    {
        public Dictionary<string, int?> items = new Dictionary<string, int?>();

        public override string group
        {
            get { return "ui/equip"; }
        }
    }
}
