using System.Collections.Generic;

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - слои карты
	/// </summary>
	[System.Serializable]
	public class Layer
	{
		public string name;
		public bool visible = true;
		public float opacity = 1;
		public int isSpawn = 0;

		public float offsetx;
		public float offsety;
		public float offsetz;

		public string resource;

		// строго словарь так ключ это порядковый номер на карте и при отправке пакета мы пустые клчи удаляем так что они могу  не порядку идти хотя изначально по прдяку с пустыми клетками
		public Dictionary<int, LayerTile> tiles;
		public LayerObject[] objects;
	}
}