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
        public const string GROUP = "ui/loot";
        public const string ACTION = "open";

        public string? key = null;

        public LootOpenResponse()
        {
            action = ACTION;
        }

        public override string group
        {
            get { return GROUP; }
        }
    }
}
