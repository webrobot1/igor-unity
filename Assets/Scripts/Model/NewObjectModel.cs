using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MyFantasy
{
	public class NewObjectModel : ObjectModel
	{
		protected Animator anim = null;
		protected static Dictionary<string, bool> trigers;

		/// <summary>
		/// если не null - движемся
		/// </summary>
		private Coroutine moveCoroutine = null;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		private int? layerIndex = null;
		private string layerTrigger = null;

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		private DateTime activeLast = DateTime.Now;

		protected virtual void Awake()
		{
			if (anim = GetComponent<Animator>())
			{
				// сохраним все возможные Тригеры анимаций и, если нам пришел action как тигер - обновим анимацию
/*				if (trigers == null)
				{
					trigers = new Dictionary<string, bool>();
					foreach (var parameter in anim.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger))
					{
						trigers.Add(parameter.name, true);
					}
				}*/
			}
		}

		// Update is called once per frame
		void Update()
		{
			// если текущий наш статус анимации - не стояние и давно небыло активности - включим анмацию остановки
			if (anim!=null && layerIndex != null && anim.GetLayerName((int)layerIndex) != "idle"  && (anim.GetCurrentAnimatorStateInfo((int)layerIndex).loop || anim.GetCurrentAnimatorStateInfo((int)layerIndex).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1)
			{
				Debug.Log("остановка по таймауту анимации");
				Animate("idle", side);
			}
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		protected void SetData(NewObjectRecive recive)
		{
			base.SetData(recive);

			// если мы двигаемся и пришли новые координаты - то сразу переместимся на локацию к которой идем
			if (recive.x != null || recive.y != null || recive.z != null)
			{
				// остановим корутину движения
				if (moveCoroutine != null)
					StopCoroutine(moveCoroutine);

				if (recive.action == "move")
					moveCoroutine = StartCoroutine(Move(position, recive.action));
				else
					transform.position = position;
			}

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			if (recive.action != null || recive.side != null )
			{
				Animate(action, side);
			}
		}

		protected void Animate(string layer, string side)
		{
			if (anim != null)
			{
				int layerIndex = anim.GetLayerIndex(layer);
				if (layerIndex != -1)
				{
					this.layerIndex = layerIndex;
					activeLast = DateTime.Now;

                    // "остановим" все слои анмиации
                    if (anim.layerCount > 1) 
					{ 
						for (int i = 1; i < anim.layerCount; i++)
						{
							anim.SetLayerWeight(i, 0);
						}
					}

					anim.SetLayerWeight(layerIndex, 1);

					// Так же работает с Blend Tree
					switch (side)
					{
						case "left":
							anim.SetFloat("x", -1);
							anim.SetFloat("y", 0);
							break;
						case "right":
							anim.SetFloat("x", 1);
							anim.SetFloat("y", 0);
							break;
						case "up":
							anim.SetFloat("x", 0);
							anim.SetFloat("y", 1);
							break;					
						case "up_left":
							anim.SetFloat("x", -0.5f);
							anim.SetFloat("y", 0.5f);
							break;						
						case "up_right":
							anim.SetFloat("x", 0.5f);
							anim.SetFloat("y", 0.5f);
							break;
						case "down":
							anim.SetFloat("x", 0);
							anim.SetFloat("y", -1);
							break;					
						case "down_left":
							anim.SetFloat("x", -0.5f);
							anim.SetFloat("y", -0.5f);
							break;				
						case "down_right":
							anim.SetFloat("x", 0.5f);
							anim.SetFloat("y", -0.5f);
							break;
					}

/*					string trigger = layer + "_" + side;
					if (this.layerTrigger == trigger) return;
					if (trigers.ContainsKey(trigger))
					{
						
						this.layerTrigger = trigger;
						anim.SetTrigger(trigger);
						Debug.Log("Обновляем анимацию " + trigger);
					}
					else
					{
						PlayerController.Instance.Error("Отсутвует тригер анмиации " + trigger + " у слоя анмиации " + layer);
					}*/
				}
				else
				{
					Debug.LogWarning("Положение без группы-слоя анимации " + layer);
				}
			}		
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Move(Vector3 position, string group)
		{
			float distancePerUpdate = Vector3.Distance(transform.position, position) / ((float)getEvent(group).timeout / Time.fixedDeltaTime);

			float distance;
			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком

				transform.position = Vector3.MoveTowards(transform.position, position, (distance < distancePerUpdate ? distance : distancePerUpdate));
				activeLast = DateTime.Now;

				yield return new WaitForFixedUpdate();
			}

			moveCoroutine = null;
		}
	}
}
