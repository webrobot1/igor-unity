using UnityEngine;

namespace MyFantasy
{
	public class PlayerModel : EnemyModel
	{
		private string login;

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((PlayerRecive)recive);
		}		
		
		private void SetData(PlayerRecive recive)
		{
			if (recive.login!=null)
				this.login = recive.login;

			base.SetData(recive);
		}	
	}
}
