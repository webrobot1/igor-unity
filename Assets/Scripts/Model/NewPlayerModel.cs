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

			// если мы умерли станем полупрозрачными. воскреились - станем нормальынми
			if (recive.components != null && recive.components.hp != null)
			{
				if (lifeBar.hp == 0 && recive.components.hp > 0)
				{
					sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 1f);
				}
				else if (recive.components.hp == 0)
				{
					sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0.5f);
				}
			}

			base.SetData(recive);
		}	
	}
}
