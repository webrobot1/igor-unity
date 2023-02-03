using UnityEngine;

namespace MyFantasy
{
	public class PlayerModel : EnemyModel
	{
		private string login;

		public void SetData(PlayerRecive data)
		{
			Debug.Log("sdf");
			if (data.login!=null)
				this.login = data.login;

			base.SetData(data);
		}	
	}
}
