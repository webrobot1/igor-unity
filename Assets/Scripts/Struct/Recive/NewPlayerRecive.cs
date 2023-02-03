namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных - игрок
	/// </summary>
	[System.Serializable]
	public class NewPlayerRecive : NewEnemyRecive
	{
		public string login;
		public NewPlayerComponentsRecive components;
	}
}

