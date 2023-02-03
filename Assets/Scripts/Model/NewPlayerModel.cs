using UnityEngine;

namespace MyFantasy
{
	public class NewPlayerModel : NewEnemyModel
	{
		private string login;

		public void SetData(NewPlayerRecive data)
		{
			Debug.Log("dsf");
			if (data.login!=null)
				this.login = data.login;

			base.SetData(data);
		}	
	}
}
