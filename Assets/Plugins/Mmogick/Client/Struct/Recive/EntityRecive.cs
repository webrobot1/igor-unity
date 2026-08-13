using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - объект
	/// </summary>
	[System.Serializable]
	public class EntityRecive
	{
		public float? x;
		public float? y;
		public float? z;

		public int? map = null;
		public string prefab;
		// Идентичность сущности с сервера: у моба/NPC — slug git-цикла, у игрока — логин аккаунта.
		public string slug;
		public string action;

		public float? forwardX = null;
		public float? forwardY = null;
		public float? forwardZ = null;
	
		public int? sort = null;
		public int? lifeRadius = null;

		/// <summary>
		/// Когда запись сущности заведена на сервере, строка ISO-8601. У игрока это дата регистрации аккаунта.
		/// Приходит один раз, при появлении сущности: значение неизменно.
		/// </summary>
		public string created = null;

		/// <summary>
		/// Когда данные сущности меняли в последний раз, строка ISO-8601. У существа — правка автором
		/// (админка либо инструменты), у игрока — его последний вход в игру. Движение сущности по карте
		/// и бой отметку не двигают.
		/// </summary>
		public string updated = null;


		public Dictionary<string, EventRecive> events;

		/// <summary>
		/// тк в каждой игре свои компоненты и разного типа (строки, цифры и даже массивы) то этот класс нуждается в переопределелни (отнаследоваться и указать свой класс этому полю). Компоненты придут на следующем кадре если авторизация с другого устройства
		/// </summary>
		public JObject components = null;	
	}
}