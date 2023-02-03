using System.Collections.Generic;

namespace MyFantasy
{
	/// <summary>
	/// Структура полученных данных - игрок
	/// </summary>
	[System.Serializable]
	public class PlayerRecive : EnemyRecive
	{
		public string login;
		
		public new ComponentsRecive components;
	}
}

