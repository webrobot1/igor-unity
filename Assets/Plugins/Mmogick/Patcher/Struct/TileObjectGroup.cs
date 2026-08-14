using System.Collections.Generic;

namespace Mmogick
{
		[System.Serializable]
		public class TileObjectGroup
		{
			public string name;
			public TileObject[] @object;
			// Тип группы из редактора карт: свободная пометка автора, клиент по ней ничего не решает.
			// Объявлена, потому что разбор строгий — неизвестное поле роняет весь тайлсет, а с ним вход
			// в игру. Имя поля совпадает с ключевым словом языка, отсюда собачка.
			public string @class;
			// Сервер хранит property с indexBy='name' → JSON-объект {name: TileProperty}.
			public Dictionary<string, TileProperty> property;
		}
}
