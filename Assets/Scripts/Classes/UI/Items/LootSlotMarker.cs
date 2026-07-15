using UnityEngine;

namespace Mmogick
{
	// Метка «этот слот принадлежит окну контейнера (лут трупа/сундука)», вешается рядом
	// с базовым SlotScript при создании слотов LootWindowController.
	// НЕ наследник SlotScript: клик-взятие в курсор у базового слота уже нужный, а
	// принадлежность контейнеру и номер позиции Item.Use определяет этой меткой
	// (GetComponentInParent<LootSlotMarker>) — и у слота-цели, и у родителя несомого Item.
	public class LootSlotMarker : MonoBehaviour
	{
		// позиция в инвентаре контейнера (1-based)
		public int Num;
	}
}
