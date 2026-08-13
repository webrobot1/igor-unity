namespace Mmogick
{
	/// <summary>
	/// Соседняя карта, примыкающая к текущей в открытом мире. x/y — смещение её левого-верхнего угла
	/// относительно текущей карты в тайлах: знак задаёт сторону (y&gt;0 — сверху, y&lt;0 — снизу,
	/// x&gt;0 — справа, x&lt;0 — слева), у самой текущей карты смещение нулевое.
	/// </summary>
	[System.Serializable]
	public class MapSide
	{
		/// <summary>
		/// Имя соседней локации — показывается в отладочном окне рядом с номером карты.
		/// </summary>
		public string name;

		public float x;
		public float y;

		/// <summary>
		/// Габариты соседа в тайлах: ими считается, попадает ли точка в его границы при склейке.
		/// </summary>
		public float width;
		public float height;

		new public string ToString()
		{
			return x + "," + y;
		}
	}
}
