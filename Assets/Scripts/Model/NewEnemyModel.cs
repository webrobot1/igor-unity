using UnityEngine;

namespace MyFantasy
{
	/// <summary>
	/// объекты могут быть не анимированы. враги и игроки что анследуют этот класс - обязательно должны иметь анмицию + модель статистики (жизни и тп)
	/// </summary>
	[RequireComponent(typeof(Animator))]
	[RequireComponent(typeof(StatModel))]
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

		public int hp
        {
			get { return statModel.hp; }
			set { statModel.hp = value; }
		}		
		
		protected int hpMax
        {
			get { return statModel.hpMax; }
			set { statModel.hpMax = value; }
		}		
		
		protected int mp
        {
			get { return statModel.mp; }
			set { statModel.mp = value; }
		}	
		
		protected int mpMax
        {
			get { return statModel.mpMax; }
			set { statModel.mpMax = value; }
		}

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
                {
					if (hp == 0 && recive.components.hp > 0)
					{
						Resurrect();
					}
					else if (recive.components.hp == 0)
					{
						Dead();
					}
					hp = (int)recive.components.hp;
                }
					

				if (recive.components.hpMax != null)
					hpMax = (int)recive.components.hpMax;

				if (recive.components.mp != null)
					mp = (int)recive.components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (recive.components.mpMax != null)
					mpMax = (int)recive.components.mpMax;
			}

			base.SetData(recive);
		}

		protected virtual void Dead()
        {
			
        }		
		
		protected virtual void Resurrect()
        {
			
		}
	}
}
