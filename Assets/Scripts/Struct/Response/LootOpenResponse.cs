#nullable enable

namespace Mmogick
{
    // Открытие контейнера key (труп существа либо объект-сундук): запрос его добычи. Состав приватен —
    // сервер шлёт его адресно world-дельтой самого контейнера, отдельного ответа на команду нет.
    // Не на клетке контейнера — сервер сам ведёт игрока к нему и повторяет открытие до прибытия.
    // Своя группа (не ui/inventory): открытие с подходом висит до прибытия и не должно занимать
    // очередь инвентарных операций игрока.
    public class LootOpenResponse : Response
    {
        public string? key = null;

        public LootOpenResponse()
        {
            action = "open";
        }

        public override string group
        {
            get { return "ui/loot"; }
        }
    }
}
