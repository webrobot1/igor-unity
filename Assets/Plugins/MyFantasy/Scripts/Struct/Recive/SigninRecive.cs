namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных при авторизации
	/// </summary>
	[System.Serializable]
	public class SigninRecive : Recive
	{
		public string host;

		public string key;
		public string token;
	}
}
