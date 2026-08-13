namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных - игрок
	/// </summary>
	[System.Serializable]
	public class PlayerRecive : CreatureRecive
	{
		public new PlayerComponentsRecive components = null;

		/// <summary>
		/// Адрес подключения игрока: сервер знает его из самого соединения и кладёт в поля игрока
		/// (класс Player фреймворка). Приходит у каждого игрока карты, не только у своего.
		/// </summary>
		public string ip = null;
	}
}

