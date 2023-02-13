using UnityEngine;

namespace MyFantasy
{
	public class NewEnemyModel : NewObjectModel
	{
		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		/// <summary>
		/// модель расчета фигурок жизней и маны
		/// </summary>
		protected StatModel statModel;

		protected override void Awake()
		{
			statModel = GetComponent<StatModel>();
			base.Awake();
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewEnemyRecive)recive);
		}

		protected void SetData(NewEnemyRecive recive)
        {
			if (recive.components != null)
			{
				if (recive.components.speed != null)
					speed = (int)recive.components.speed;

				if (recive.components.hp != null)
					statModel.hp = (int)recive.components.hp;

				if (recive.components.hpMax != null)
					statModel.hpMax = (int)recive.components.hpMax;

				if (recive.components.mp != null)
					statModel.mp = (int)recive.components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (recive.components.mpMax != null)
					statModel.mpMax = (int)recive.components.mpMax;
			}

			base.SetData(recive);
		}
	}
}
