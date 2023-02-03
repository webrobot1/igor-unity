using UnityEngine;

namespace MyFantasy
{
	public class NewPlayerModel : NewEnemyModel
	{
		private string login;

		public void SetData(NewPlayerRecive data)
		{
			if (data.login != null)
				this.login = data.login;

			if (data.components != null && data.components.hp != null)
			{
				if (lifeBar.hp == 0 && data.components.hp > 0)
				{
					sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 1f); // (r,g,b,a); последний параметр прозрачность. От 0 до 1.
				}
				else if (data.components.hp == 0)
				{
					sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0.5f);
				}
			}		

			base.SetData(data);
		}	
	}
}
