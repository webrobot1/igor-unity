using System.Collections.Generic;

/// <summary>
/// ������� ����������� 
/// </summary>
[System.Serializable]
public class Tileset
{
	public int tileset_id;
	public int firstgid;
	public int columns;
	public int tilecount;
	public int tilewidth;
	public int tileheight;


	public int spacing;
	public int margin;
	public string trans;

	public string resource;

	// ������ ������� �� ��� ���� ����� ��� ������� ����� tileset �� layer 
	public Dictionary<int, TilesetTile> tile = new Dictionary<int, TilesetTile> { };
}