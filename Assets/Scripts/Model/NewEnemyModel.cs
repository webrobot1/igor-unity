using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MyFantasy
{
	/// <summary>
	/// объекты могут быть не анимированы. враги и игроки что анследуют этот класс - обязательно должны иметь анмицию + модель статистики (жизни и тп)
	/// </summary>
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


        protected void FixedUpdate()
        {
			// если пришли данные атаки и мы до сих пор атакуем
			if (key != PlayerController.player.key && getEvent("attack").action != null && getEvent("attack").action.Length > 0)
			{
				if (PlayerController.target == null || (PlayerController.target.key!=key && (Vector3.Distance(PlayerController.target.position, PlayerController.player.position)) > (Vector3.Distance(position , PlayerController.player.position))))
				{
					//  и атакуем нашего игрока у игрока нетц ели атаки
					string new_target = getEventData<AttackDataRecive>("attack").target;
					if (new_target != null && new_target == PlayerController.player.key)
					{
						// то передадим инфомрацию игроку что бы мы стали его целью
						PlayerController.Select(key);
						Debug.LogWarning("Сущность " + key + " атакует нас, установим ее как цель цель");
					}
				}
			}
		}

        protected void SetData(NewEnemyRecive recive)
        {
			if (recive.components != null)
			{
				if (recive.components.speed != null)
				{
					speed = (int)recive.components.speed;
					//anim.speed = speed;
				}

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

		public T getEventData<T>(string group) where T : new()
		{
			EventRecive ev = base.getEvent(group);
			return ev.data != null ? ev.data.ToObject<T>() : new T();
		}

		protected virtual void Dead()
        {
			
        }		
		
		protected virtual void Resurrect()
        {
			
		}
	}
}
