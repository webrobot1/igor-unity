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


	public TilesetTile(Sprite sprite)
    {
		this.sprite = sprite;
	}
}