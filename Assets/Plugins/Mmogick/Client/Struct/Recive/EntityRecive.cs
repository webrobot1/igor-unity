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

		public int? map_id = null;
		public string prefab;
		public string login;
		public string action;

		public float? forward_x = null;
		public float? forward_y = null;
	
		public int? sort = null;
		public int? lifeRadius = null;
		

		public DateTime? created = null;
		
		public Dictionary<string, EventRecive> events;

		/// <summary>
		/// тк в каждой игре свои компоненты и разного типа (строки, цифры и даже массивы) то этот класс нуждается в переопределелни (отнаследоваться и указать свой класс этому полю). Компоненты придут на следующем кадре если авторизация с другого устройства
		/// </summary>
		public JObject components = null;	
	}
}