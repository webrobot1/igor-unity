using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;

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
	public int sort = 0;

	public bool ground;

	public float offsetx;
	public float offsety;

	public string resource;

	[JsonConstructor]
	public Layer (dynamic tiles = null)
    {
		if (tiles != null)
		{
			this.tiles = new Dictionary<int, LayerTile> { };
			if (tiles.GetType() == typeof(JArray))
			{
				JArray jarray = tiles;
				this.tiles = jarray.ToDictionary(k => jarray.IndexOf(k), v => v.ToObject<LayerTile>());
			}
			else
			{
				JObject jobject = tiles;
				this.tiles = jobject.ToObject<Dictionary<int, LayerTile>>();
			}
		}
	}

	// строго словарь так ключ это порядковый номер на карте и при отправке пакета мы пустые клчи удаляем так что они могу  не порядку идти хотя изначально по прдяку с пустыми клетками
	public dynamic tiles;
	public Dictionary<int, LayerObject> objects;
}