namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных - произвольыне поля
	/// </summary>
	[System.Serializable]
	public class NewComponentsRecive
	{
		/// <summary>
		/// если не приравнять к null - будет  0 при наличии другого лбого элемента класса
		/// </summary>
		public int? hp;
		public int? hpmax;	
	
		public int? mp;
		public int? mpmax;

		public int? speed;
	}
}