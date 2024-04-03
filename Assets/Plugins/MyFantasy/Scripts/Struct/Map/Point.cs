namespace MyFantasy
{
	/// <summary>
	/// объекты на слое (полигоны, текс, картинки)
	/// </summary>

	[System.Serializable]
	public class Point
	{
		public float x;
		public float y;

		public string ip;
		public int port;

		new public string ToString()
        {
			return x + "," + y;
        }
	}
}