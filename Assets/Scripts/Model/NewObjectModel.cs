using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyFantasy
{

	/// <summary>
	/// колайдер обязателен тк мы кликаем на gameObject что бы выделить его  в область колайдера
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public class NewObjectModel : ObjectModel
	{
		protected Animator anim = null;
		//protected static Dictionary<string, bool> trigers;

		/// <summary>
		/// если не null - движемся
		/// </summary>
		private Coroutine moveCoroutine = null;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		private int? layerIndex = null;

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		private DateTime activeLast = DateTime.Now;

		/// <summary>
		///  это сторона движения игркоа. как transform forward ,  автоматом нормализует значения
		/// </summary>
		public override Vector3 forward
		{
			get { return base.forward; }
			set 
			{
				// вообще сервер сам это сделает но так уменьшиться пакет размера символов
				base.forward = value.normalized;
				if (anim)
				{
					if (anim.GetFloat("x") != value.x)
						anim.SetFloat("x", value.x);
					if (anim.GetFloat("y") != value.y)
						anim.SetFloat("y", value.y);
				}
			}
		}
		protected static Dictionary<string, bool> trigers;

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
			// если текущий наш статус анимации - не стояние и давно небыло активности - включим анмацию остановки
			if (
				anim!=null 
					&& 
				layerIndex != null 
					&& 
				anim.GetLayerName((int)layerIndex) != "idle"  
					&& 				
				action != "dead"  
					&& 
				(anim.GetCurrentAnimatorStateInfo((int)layerIndex).loop || anim.GetCurrentAnimatorStateInfo((int)layerIndex).normalizedTime >= 1.0f) 
					&& 
				DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1
			)
			{
				Debug.Log(key+" остановка по таймауту анимации");
				Animate("idle");
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
				{
					StopCoroutine(moveCoroutine);
				}

				if (recive.action == "move")
					moveCoroutine = StartCoroutine(Move(position, recive.action));
				else
					transform.position = position;
			}

			// следующий код применим только к объектам - предметам, он повернет их
            if(this.GetType().Name == "NewObjectModel")
			{ 
				float angle = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg*-1;
				transform.rotation = Quaternion.Euler(0, 0, angle);
			}

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			if (recive.action != null)
			{
				Animate(action);
				activeLast = DateTime.Now;
			}
		}

		protected void Animate(string name)
		{
			if (anim != null)
			{
				
				int layerIndex = anim.GetLayerIndex(name);
				if (layerIndex != -1)
				{
					if(this.layerIndex != layerIndex) 
					{ 
						// "остановим" все слои анмиации
						if (anim.layerCount > 1) 
						{ 
							for (int i = 1; i < anim.layerCount; i++)
							{
								anim.SetLayerWeight(i, 0);
							}
						}

						anim.SetLayerWeight(layerIndex, 1);
						this.layerIndex = layerIndex;
					}

					if (trigers.ContainsKey(name))
					{
						Debug.Log("запускаем тригер " + name);
						anim.SetTrigger(name);
					}
				}
				else
				{
					Debug.LogWarning("Положение без группы-слоя анимации " + name);
				}
			}		
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Move(Vector3 position, string group)
		{
			yield return new WaitForFixedUpdate();

			float distance;

			// Здесь экстрополяция - на сервере игрок уже может и дошел но мы продолжаем двигаться (используется таймаут а не фактическое оставшееся время тк при большом пинге игрок будет скакать)
			double distancePerUpdate = Vector3.Distance(transform.position, position) / ((getEvent(group).timeout ?? GetEventRemain(group)) / Time.fixedDeltaTime);

			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком

				transform.position = Vector3.MoveTowards(transform.position, position, (float)(distance < distancePerUpdate ? distance : distancePerUpdate));
				activeLast = DateTime.Now;

				yield return new WaitForFixedUpdate();
			}

			moveCoroutine = null;
		}
	}
}
