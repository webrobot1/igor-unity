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
		protected Coroutine moveCoroutine = null;

		protected virtual void Awake()
		{
			if (anim = GetComponent<Animator>())
			{
				// сохраним все возможные Тригеры анимаций и, если нам пришел action как тигер - обновим анимацию
				if (trigers == null)
				{
					trigers = new Dictionary<string, bool>();
					foreach (var parameter in anim.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger))
					{
						trigers.Add(parameter.name, true);
					}
				}
			}
		}

		// Update is called once per frame
		void Update()
		{
			// если мы не стоит и нет корутины что двигаемся и мы не жде ответа от сервера о движении (актуально лишь на нашего игрока)
			if (anim!=null && anim.GetCurrentAnimatorClipInfo(0)[0].clip.name.IndexOf("idle") == -1 && moveCoroutine == null && (anim.GetCurrentAnimatorStateInfo(0).loop || anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1)
			{
				Debug.LogWarning("останавливаем " + this.key);
				action = "idle_"+side;
				anim.SetTrigger(action);
			}
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		protected void SetData(NewObjectRecive recive)
		{
			string trigger;

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			if (recive.action != null || recive.side != null )
			{
				trigger = (recive.action != null ? recive.action : action) + "_" + (recive.side != null ? recive.side : side);
				if(anim!=null)
                {
					if (trigers.ContainsKey(trigger)) 
					{ 
						Debug.Log("Обновляем анимацию " + trigger);
						anim.SetTrigger(trigger);
					}
					else
					{
						Debug.LogWarning("Положение без анимации " + trigger);
						trigger = "idle_" + (recive.side != null ? recive.side : side);
						anim.SetTrigger(trigger);
					}
				}    
			}

			if (this.key.Length > 0 && (recive.x != null || recive.y != null || recive.z != null))
			{
				if (moveCoroutine != null)
					StopCoroutine(moveCoroutine);

				Vector3 moveTo = new Vector3((float)(recive.x != null ? recive.x : transform.position.x), (float)(recive.y != null ? recive.y : transform.position.y), (float)(recive.z != null ? recive.z : transform.position.z));

				// todo пока 1 шаг - 1 единици позиции  и если больше - это не ходьба а телепорт. в будушем может быть меньше 1 единицы
				// если отставание от текущей позиции больше чем полтора шага телепортнем (а може это и есть телепорт)
				if (Vector3.Distance(moveTo, transform.position) <= 1.5)
					moveCoroutine = StartCoroutine(Move(moveTo));
				else
					transform.position = moveTo;

				recive.x = recive.y = recive.z = null;
			}

			base.SetData(recive);
		}


		/// <summary>
		/// анимация движения NPC или игрока. скорость равна времени паузы между командами
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Move(Vector3 position)
		{
			float distance;

			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком

				transform.position = Vector3.MoveTowards(transform.position, position, (distance < distancePerUpdate ? distance : distancePerUpdate));
				transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

				yield return new WaitForFixedUpdate();
			}

			activeLast = DateTime.Now;
			moveCoroutine = null;
		}
	}
}
