using UnityEngine;

namespace MyFantasy
{
	public class NewPlayerModel : NewEnemyModel
	{
		private string login;

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewPlayerRecive)recive);
		}			
		
		private void SetData(NewPlayerRecive recive)
		{
			if (recive.login != null)
				this.login = recive.login;

			base.SetData(recive);
		}	
	}
}
