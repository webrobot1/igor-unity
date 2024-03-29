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
	public class EnemyModel : ObjectModel
	{
		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

        protected override void Awake()
        {
			base.Awake();
			lifeBar = GetComponentInChildren<CanvasGroup>().transform.GetChild(0).GetChild(0).GetComponent<Image>();

			// скороет если при работе со сценой забыли скрыть (оно показается только при выделении на карте существа) 
			TargetController.DisableLine(lifeBar);
		}

        protected void Start()
		{		
			if (lifeBar == null)
				PlayerController.Error("Не найдено в группе поле статистики сущности "+key);
		}

		public override void SetData(EntityRecive recive)
		{
			this.SetData((EnemyRecive)recive);
		}

        protected void SetData(EnemyRecive recive)
        {
			PrepareComponents(recive.components);
			base.SetData(recive);		
		}

		protected void PrepareComponents(EnemyComponentsRecive components)
        {
			if (components != null)
			{
				if (components.speed != null)
				{
					speed = (int)components.speed;
					//anim.speed = speed;
				}

				if (components.hp != null)
				{
					if (hp == 0 && components.hp > 0)
					{
						Resurrect();
					}
					else if (components.hp == 0)
					{
						Dead();
					}
					hp = (int)components.hp;
				}
				if (components.hpmax != null)
					hpMax = (int)components.hpmax;

				if (components.mp != null)
					mp = (int)components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (components.mpmax != null)
					mpMax = (int)components.mpmax;
			}
		}

		protected virtual void Dead()
        {
			
        }		
		
		protected virtual void Resurrect()
        {
			
		}
	}
}
