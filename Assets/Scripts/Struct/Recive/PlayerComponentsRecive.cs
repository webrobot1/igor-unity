using System.Collections.Generic;

namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных - произвольыне поля
	/// </summary>
	[System.Serializable]
	public class PlayerComponentsRecive: EnemyComponentsRecive
	{
		public Dictionary<string, string> settings = null;
		public Dictionary<string, bool> spellBook = null;
	}
}