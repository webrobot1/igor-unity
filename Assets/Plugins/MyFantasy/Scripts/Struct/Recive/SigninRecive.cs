namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных при авторизации
	/// </summary>
	[System.Serializable]
	public class SigninRecive
	{
		public string host;

		public string key;
		public string token;

		public float step;

		/// <summary>
		///  Экстраполяция - время в секундах для непрерывных событий которым стоит продлевать анимацию их движения. Спустя remain время события пакет дойдет только с сервера
		/// </summary>
		public double extrapol;

		public int position_precision;

		/// <summary>
		/// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
		/// </summary>
		public string error = "";
	}
}
