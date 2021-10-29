using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ��������� ���� � ������� ������� ����� ���� ��������� ��������
/// </summary>
[System.Serializable]
public class TilesetTile
{
	public int tile_id;
	public string resource;
	public Sprite sprite;
	public Sprite[] sprites;

	public TilesetTileAnimation[] frame;

	public TilesetTile(Sprite sprite)
    {
		this.sprite = sprite;
	}
}