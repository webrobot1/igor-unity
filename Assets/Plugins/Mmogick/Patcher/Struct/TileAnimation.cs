namespace Mmogick
{
	/// <summary>
	/// отдельный тайл в палитре который может быть отдельынм спрайтом
	/// </summary>
	[System.Serializable]
	public class TileAnimation
	{
		public string sha256;
		public int duration;

		// заполняется в процесса персинга
		public Sprite sprite;
	}
}