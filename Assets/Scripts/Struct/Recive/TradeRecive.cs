using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

#nullable enable

namespace Mmogick
{
	/// <summary>
	/// Ценник торгующего контейнера — компонент trade сущности. ПУБЛИЧНЫЙ: приходит всем видящим лавку
	/// обычной world-дельтой, ещё до того как игрок подойдёт и запросит её содержимое.
	///
	/// Торгует ли сущность, решает ЗАПОЛНЕННОСТЬ ценника (Trades), а не его наличие: компонент со
	/// структурным значением платформа материализует у каждой сущности вида object, поэтому пустой ценник
	/// приходит и порталу, и алтарю, и обычному сундуку — отличить его от «компонента нет» нечем.
	///
	/// Отдельных сделок «купить» и «продать» нет: покупка — тот же забор из контейнера, продажа — то же
	/// укладывание в него, отличается лишь плата. Цену считает сервер (он и решает, состоялась ли сделка),
	/// клиентский расчёт нужен только для показа.
	///
	///   buy   — множитель цены, по которой торговец ПРОДАЁТ игроку;
	///   sell  — множитель, по которому он СКУПАЕТ.
	///
	/// Базовой цены предметов ценник не несёт: она лежит у самого предмета (компонент price) и приезжает
	/// манифестом префабов — цена нужна и там, где торговца рядом нет вовсе.
	/// </summary>
	[System.Serializable]
	public class TradeRecive
	{
		public float buy;
		public float sell;

		/// <summary>Компонент базовой цены предмета — по нему считается цена обеих сторон сделки.</summary>
		public const string COMPONENT_PRICE = "price";

		/// <summary>
		/// Торгует ли сущность с этим ценником: заданы ОБА множителя. Пустое значение (ни наценки, ни
		/// скупки) — обычный контейнер, тем же правилом руководствуется сервер.
		/// </summary>
		public bool Trades
		{
			get { return buy > 0 && sell > 0; }
		}

		/// <summary>
		/// Цена покупки count единиц предмета игроком; null — сделки нет. Округление вверх: дробь всегда
		/// в пользу торговца — то же правило, по которому считает сервер.
		/// </summary>
		public int? BuyPrice(string prefab, Dictionary<string, string>? components, int count)
		{
			return Price(prefab, components, buy, count, true);
		}

		/// <summary>
		/// Сколько торговец даст за count единиц предмета; null — сделки нет (нет базовой цены либо скупка
		/// закрыта нулевым множителем). Округление вниз: дробь в пользу торговца.
		/// </summary>
		public int? SellPrice(string prefab, Dictionary<string, string>? components, int count)
		{
			return Price(prefab, components, sell, count, false);
		}

		/// <summary>
		/// Цена позиции: базовая × множитель своей стороны × количество. Сделки нет — null: она не
		/// состоится вовсе, когда у вещи нет базовой цены либо своя сторона закрыта нулевым множителем
		/// (даром вещь не уходит и не отдаётся).
		/// Состоявшаяся сделка дешевле монеты не бывает: округление своей стороны может дать ноль, но
		/// платят всё равно одну — тем же правилом считает сервер, и клиент обязан показывать ровно ту
		/// сумму, что спишется. Ноль как «бесплатно» отсюда наружу не уходит.
		/// Базовая — эффективная цена ЭТОГО экземпляра: своя, иначе префабная (AnimationCacheService
		/// держит само правило). components — компоненты слота хранилища, где предмет лежит; их нет
		/// (предмет в руке, у чужой стороны) — цена берётся префабная.
		/// </summary>
		private int? Price(string prefab, Dictionary<string, string>? components, float rate, int count, bool up)
		{
			if (string.IsNullOrEmpty(prefab) || count <= 0 || rate <= 0)
				return null;

			JToken? value = AnimationCacheService.GetComponentValue(prefab, COMPONENT_PRICE, components);
			if (value == null)
				return null;

			float basePrice = value.Value<float>();
			if (basePrice <= 0)
				return null;

			float total = basePrice * rate * count;

			return Mathf.Max(1, up ? Mathf.CeilToInt(total) : Mathf.FloorToInt(total));
		}
	}
}
