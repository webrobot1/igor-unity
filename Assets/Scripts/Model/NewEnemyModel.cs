using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;
using System;

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

		protected void Start()
		{
			lifeBar = GetComponentInChildren<CanvasGroup>().transform.GetChild(0).GetChild(0).GetComponent<Image>();
			if (lifeBar == null)
				PlayerController.Error("Не найдено в группе поле статистики сущности "+key);

			// скороет если при работе со сценой забыли скрыть (оно показается только при выделении на карте существа) 
			FaceAnimationController.DisableLine(lifeBar);
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


		protected virtual void Dead()
        {
			
        }		
		
		protected virtual void Resurrect()
        {
			
		}
	}
}
