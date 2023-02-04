using UnityEngine;

namespace MyFantasy
{
	public class NewEnemyModel : NewObjectModel
	{
		/// <summary>
		/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
		/// </summary>
		public LifeModel lifeBar;

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewEnemyRecive)recive);
		}

		protected void SetData(NewEnemyRecive recive)
        {
			if (recive.components != null)
			{
				if (recive.components.hp != null)
				{
					lifeBar.hp = (int)recive.components.hp;
				}
				if (recive.components.hpMax != null)
					lifeBar.hpMax = (int)recive.components.hpMax;

				if (recive.components.mp != null)
					lifeBar.mp = (int)recive.components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (recive.components.mpMax != null)
					lifeBar.mpMax = (int)recive.components.mpMax;
			}

			base.SetData(recive);
		}
	}
}
