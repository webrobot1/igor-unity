using UnityEngine;

namespace Mmogick
{
	// Метка «это окно добычи целиком», вешается на панель окна (LootWindowController.Awake).
	// Отвечает на вопрос, промахнулся ли игрок мимо ячейки, но всё же попал в окно: LootSlotMarker
	// есть только на самих ячейках, а между ними лежат отступы сетки, заголовок и фон панели.
	// Попадание в окно — то же намерение, что попадание в ячейку («отдать это сюда»), только позицию
	// игрок не выбрал: её подберёт сервер (see LootWindowController.SendPut с пустым to).
	public class LootWindowMarker : MonoBehaviour
	{
	}
}
