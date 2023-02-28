using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;

namespace MyFantasy
{
	/// <summary>
	/// объекты могут быть не анимированы. враги и игроки что анследуют этот класс - обязательно должны иметь анмицию + модель статистики (жизни и тп)
	/// </summary>
	public class NewEnemyModel : NewObjectModel
	{
		private Image health;

		public int hp;

		protected int hpMax;
		protected int mp;
		protected int mpMax;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		/// <summary>
		///  скорость изменения полоски жизней и маны
		/// </summary>
		private float lineSpeed = 3;

		CameraController cameraController;


		protected void Start()
		{
			transform.Find("LifeBar").GetComponent<CanvasGroup>().alpha = 0;

			cameraController = Camera.main.GetComponent<CameraController>();
			health = transform.Find("LifeBar").Find("Background").Find("Health").GetComponent<Image>();
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewEnemyRecive)recive);
		}


        protected void FixedUpdate()
        {
			HealthUpdate();
			ManaUpdate();

			// если пришли данные атаки и мы до сих пор атакуем
			if (PlayerController.player!=null && key != PlayerController.player.key && getEvent(AttackResponse.GROUP).action != null && getEvent(AttackResponse.GROUP).action.Length > 0)
			{
				if (PlayerController.target == null || (PlayerController.target.key!=key && (Vector3.Distance(PlayerController.target.position, PlayerController.player.position)) > (Vector3.Distance(position , PlayerController.player.position))))
				{
					//  и атакуем нашего игрока у игрока нетц ели атаки
					string new_target = getEventData<AttackDataRecive>(AttackResponse.GROUP).target;
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

		private void HealthUpdate()
		{
			float healthFill = (float)hp / (float)hpMax;
			if (healthFill != health.fillAmount) //If we have a new fill amount then we know that we need to update the bar
			{
				//Lerps the fill amount so that we get a smooth movement
				health.fillAmount = Mathf.Lerp(health.fillAmount, healthFill, Time.deltaTime * lineSpeed);
			}
			if (key == PlayerController.player_key)
			{
				if (Camera.main.GetComponent<CameraController>().hpFrame.fillAmount != healthFill)
				{
					cameraController.hpFrame.fillAmount = Mathf.Lerp(cameraController.hpFrame.fillAmount, healthFill, Time.deltaTime * lineSpeed);
					cameraController.hpFrame.GetComponentInChildren<Text>().text = hp + " / " + hpMax;
				}
			}
		}

		private void ManaUpdate()
		{
			float manaAmount = (float)mp / (float)mpMax;
			if (cameraController.mpFrame.fillAmount != mp / mpMax)
			{
				cameraController.mpFrame.fillAmount = Mathf.Lerp(cameraController.mpFrame.fillAmount, manaAmount, Time.deltaTime * lineSpeed);
				cameraController.mpFrame.GetComponentInChildren<Text>().text = mp + " / " + mpMax;
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
