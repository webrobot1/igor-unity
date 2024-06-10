using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;

namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - слои карты
	/// </summary>
	[System.Serializable]
	public class Layer
	{
		public int layer_id;
		public string name;
		public int visible = 1;
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