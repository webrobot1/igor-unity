using System;


namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных - объект
	/// </summary>
	[System.Serializable]
	public class ObjectRecive
	{
		public float? x;
		public float? y;
		public float? z;

		public int map_id;
		public string prefab;

		public string action;
		public int? sort = null;

		public float speed;

		public DateTime created = DateTime.Now;

		public ComponentsRecive components;
	}
}