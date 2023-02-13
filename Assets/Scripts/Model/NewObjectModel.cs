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
			// если мы двигались и что то нас прервало (потом уточним что может а что не может нас прерывать анимацию) то сразу переместимся на локацию к которой идем
			if (moveCoroutine != null && (recive.action != null && recive.action!=action) || (recive.side != null && recive.side!=side))
			{
				StopCoroutine(moveCoroutine);
				transform.position = position;
			}

			base.SetData(recive);

			if ((recive.x != null || recive.y != null || recive.z != null) && position != transform.position)
			{
				if (recive.action == "move")
					moveCoroutine = StartCoroutine(Move(position));
				else
					transform.position = position;
			}

			string trigger;
			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			if (recive.action != null || recive.side != null )
			{
				trigger = action + "_" + side;
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
						trigger = "idle_" + side;
						anim.SetTrigger(trigger);
					}
				}
			}
		}


		/// <summary>
		/// анимация движения NPC или игрока. скорость равна времени паузы между командами
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Move(Vector3 position)
		{
			float distance;

			float distancePerUpdate = (float)getEvent("move").timeout / Time.fixedDeltaTime;
			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком

				transform.position = Vector3.MoveTowards(transform.position, position, (distance < distancePerUpdate ? distance : distancePerUpdate));
				transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

				Debug.Log(GetEventRemain("move"));
				yield return new WaitForFixedUpdate();
			}

			activeLast = DateTime.Now;
			moveCoroutine = null;
		}
	}
}
