using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyFantasy
{

	/// <summary>
	/// колайдер обязателен тк мы кликаем на gameObject что бы выделить его  в область колайдера
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public class NewObjectModel : ObjectModel
	{
		public Animator animator;

		/// <summary>
		/// если не null - движемся
		/// </summary>
		private Coroutine moveCoroutine;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		[NonSerialized]
		public int layerIndex = 0;

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		private DateTime activeLast = DateTime.Now;


		/// <summary>
		/// может быть null если мы через этот класс выделилил объект оно именно тут для совместимости как и то что ниже
		/// </summary>
		[NonSerialized]
		public int? hp = null;
		[SerializeField]
		protected Image health;

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
		///  скорость изменения полоски жизней и маны
		/// </summary>
		private static float lineSpeed = 3;

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
				if (animator)
				{
					if (animator.GetFloat("x") != value.x)
						animator.SetFloat("x", value.x);
					if (animator.GetFloat("y") != value.y)
						animator.SetFloat("y", value.y);
				}
			}
		}
		private static Dictionary<string, bool> trigers;

		protected virtual void Awake()
		{
			if (animator = GetComponent<Animator>())
			{
				// сохраним все возможные Тригеры анимаций и, если нам пришел action как тигер - обновим анимацию
				if (trigers == null)
				{
					trigers = new Dictionary<string, bool>();
					foreach (var parameter in animator.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger))
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
				animator != null 
					&& 
				animator.GetLayerName(layerIndex) != "idle"  
					&& 				
				action != "dead"  
					&& 
				(animator.GetCurrentAnimatorStateInfo(layerIndex).loop || animator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1.0f) 
					&& 
				DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1
			)
			{
				Debug.Log(key+" остановка по таймауту анимации");
				Animate(animator, animator.GetLayerIndex("idle"));
			}
		}

		public void FillUpdate(Image line, float current, float max, Text text = null, bool force = false)
		{
			line.transform.parent.gameObject.SetActive(true);
			float newFill = current / max;
			if (newFill != line.fillAmount) //If we have a new fill amount then we know that we need to update the bar
			{
				if (force)
					line.fillAmount = newFill;
				else
					line.fillAmount = Mathf.Lerp(line.fillAmount, newFill, Time.deltaTime * lineSpeed);
				if (text != null)
					text.text = current + " / " + max;
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

				if (recive.action == ConnectController.ACTION_REMOVE)
					recive.action = "walk";

				if (recive.action == "walk" && position != transform.position)
					moveCoroutine = StartCoroutine(Walk(position));
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
				if (animator != null)
				{
					int layerIndex = animator.GetLayerIndex(recive.action);
					if(layerIndex==-1)
					{
						Debug.LogWarning("Положение без группы-слоя анимации ");
					}
					else
					{
						Animate(animator, layerIndex);
					}
				}
				activeLast = DateTime.Now;
			}
		}

		public void Animate(Animator animator, int layerIndex)
		{
			if (layerIndex >=0)
			{
				if (layerIndex == 0 || animator.GetLayerWeight(layerIndex) != 1) 
				{ 
					// "остановим" все слои анмиации
					if (animator.layerCount > 1) 
					{ 
						for (int i = 1; i < animator.layerCount; i++)
						{
							animator.SetLayerWeight(i, 0);
						}
					}
					animator.SetLayerWeight(layerIndex, 1);		
				}
				this.layerIndex = layerIndex;

				string name = animator.GetLayerName(layerIndex);
				if (trigers.ContainsKey(name))
				{
					Debug.Log("запускаем тригер " + name);
					animator.SetTrigger(name);
				}
			}
			else
				PlayerController.Error("неверный индекс анимации "+ layerIndex);
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Walk(Vector3 position)
		{
			yield return new WaitForFixedUpdate();

			float distance;

			// Здесь экстрополяция - на сервере игрок уже может и дошел но мы продолжаем двигаться (используется таймаут а не фактическое оставшееся время тк при большом пинге игрок будет скакать)
			double distancePerUpdate = Vector3.Distance(transform.position, position) / ((getEvent(WalkResponse.GROUP).timeout ?? GetEventRemain(WalkResponse.GROUP)) / Time.fixedDeltaTime);

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
