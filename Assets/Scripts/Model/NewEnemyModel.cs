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
		[SerializeField]
		private CanvasGroup lifeBar;
		[SerializeField]
		private Image health;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект
		/// </summary>
		[NonSerialized]
		public int? hp = null;
		/// <summary>
		/// может быть null если мы через этот класс выделилил объект
		/// </summary>
		[NonSerialized]
		public int? mp = null;

		[NonSerialized]
		public int hpMax;
		[NonSerialized]
		public int mpMax;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		/// <summary>
		///  скорость изменения полоски жизней и маны
		/// </summary>
		private static float lineSpeed = 3;

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

			// если пришли данные атаки и мы до сих пор атакуем
			if (PlayerController.Instance.player !=null && key != PlayerController.Instance.player.key && getEvent(AttackResponse.GROUP).action != null && getEvent(AttackResponse.GROUP).action.Length > 0)
			{
				if (PlayerController.Instance.target == null || (PlayerController.Instance.target.key!=key && (Vector3.Distance(PlayerController.Instance.target.position, PlayerController.Instance.player.position)) > (Vector3.Distance(position , PlayerController.Instance.player.position))))
				{
					//  и атакуем нашего игрока у игрока нетц ели атаки
					string new_target = getEventData<AttackDataRecive>(AttackResponse.GROUP).target;
					if (new_target != null && new_target == PlayerController.Instance.player.key)
					{
						// то передадим инфомрацию игроку что бы мы стали его целью
						PlayerController.Instance.SelectTarget(key);
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

		public void FillUpdate(Image line, float current, float max, Text text = null)
        {
            float newFill = current / max;
            if (newFill != line.fillAmount) //If we have a new fill amount then we know that we need to update the bar
            {
				line.fillAmount = Mathf.Lerp(line.fillAmount, newFill, Time.deltaTime * lineSpeed);
				if(text!=null)
					text.text = current + " / " + max;
            }
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
