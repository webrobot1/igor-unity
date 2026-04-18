namespace Mmogick
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
		public int fps;

		public int map;
		public int game;

		public int position_precision;

		// Маппинг action→clip per entity, приходит инлайном с авторизацией вместо отдельного /entity_actions.
		// Передаётся параметром в ConnectController.Connect, хранится в ConnectController.entity_actions.
		public System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, string>> entity_actions;

		/// <summary>
		/// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
		/// </summary>
		public string error = "";
	}
}
