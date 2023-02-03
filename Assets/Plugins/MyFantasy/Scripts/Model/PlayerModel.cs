using UnityEngine;

namespace MyFantasy
{
	public class PlayerModel : EnemyModel
	{
		private string login;

		public void SetData(PlayerRecive data)
		{
			
			if (data.login!=null)
				this.login = data.login;

			base.SetData(data);
		}	
	}
}
