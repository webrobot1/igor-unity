using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;

/// <summary>
/// ��������� ���������� ������ - ���� �����
/// </summary>
[System.Serializable]
public class Layer
{
	public int layer_id;
	public string name;
	public int visible = 1;
	public float opacity = 1;

	public float offsetx;
	public float offsety;

	public string resource;

	[JsonConstructor]
	public Layer (dynamic tiles = null)
    {
		if (tiles != null && tiles.GetType() == typeof(JArray))
		{
			JArray jarray = tiles;		
			this.tiles = jarray.ToDictionary(k => jarray.IndexOf(k), v => v.ToObject<LayerTile>());
		}
    }

	// ������ ������� ��� ���� ��� ���������� ����� ����� �� ����� � ��� �������� ������ �� ������ ���� ������� ��� ��� ��� ����  �� ������� ���� ���� ���������� �� ������ � ������� ��������
	public dynamic tiles = new Dictionary<int, LayerTile> { };
	public Dictionary<int, LayerObject> objects = new Dictionary<int, LayerObject> { };
}