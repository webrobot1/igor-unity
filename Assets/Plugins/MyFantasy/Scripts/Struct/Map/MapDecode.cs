namespace MyFantasy
{
	/// <summary>
	/// —труктура полученных данных - карты
	/// </summary>
	[System.Serializable]
	public class MapDecode
	{
		public int map_id;
		public int width;
		public int height;
		public int spawn_sort;

		public MapDecode(Map map)
		{
			this.map_id = map.map_id;
			this.spawn_sort = (int)map.spawn_sort;
			this.width = map.width;
			this.height = map.height;
		}
	}
}