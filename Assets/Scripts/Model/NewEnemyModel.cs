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
			if (health == null)
				PlayerController.Error("Не найдено в группе поле жизней сущности "+key);

			health.GetComponentInParent<CanvasGroup>().alpha = 0;
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewEnemyRecive)recive);
		}

        protected void FixedUpdate()
        {
			if(hp!=null)
				FillUpdate(health, (int)hp, hpMax);

			// если существо атакует игрока и игроку можно установить эту цель (подробнее в функции SelectTarget) - установим
			if (
				PlayerController.Instance.player != null
					&&
				PlayerController.Instance.CanBeTarget(this)
					&&
				getEventData<AttackDataRecive>(AttackResponse.GROUP).target == PlayerController.Instance.player.key)
			{
				// то передадим инфомрацию игроку что бы мы стали его целью
				PlayerController.Instance.SelectTarget(this);
				Debug.LogWarning("Сущность " + key + " атакует нас, установим ее как цель цель");
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
