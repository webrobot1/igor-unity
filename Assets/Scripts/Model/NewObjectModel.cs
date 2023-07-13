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
	/// колайдер обязателен тк мы кликаем на gameObject что бы выделить его  в область колайдера. этот клас наследуется от плагина и реулизует работу с анимацией. вы можете реализовать по своему (поэтому работа с ней не часть плагина)
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public class NewObjectModel : ObjectModel
	{

		[NonSerialized]
		public Animator animator;

		/// <summary>
		/// список анимационных тригеров
		/// </summary>
		private static Dictionary<string, bool> trigers;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		[NonSerialized]
		public int layerIndex = 0;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект оно именно тут для совместимости как и то что ниже
		/// </summary>
		[NonSerialized]
		public int? hp = null;

		/// <summary>
		/// поле с жизнями выделленого существа
		/// </summary>
		[NonSerialized]
		public Image lifeBar;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект
		/// </summary>
		[NonSerialized]
		public int? mp = null;

		[NonSerialized]
		public int hpMax;

		[NonSerialized]
		public int mpMax;

		private Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

		/// <summary>
		///  это сторона движения игркоа. как transform forward ,  автоматом нормализует значения
		/// </summary>
		public override Vector3 forward
		{
			get { return base.forward; }
			set 
			{
				// вообще сервер сам нормализует но так уменьшиться пакет размера символов
				base.forward = value.normalized;

				//это Blend tree аниматора (в игре Игорья решил так вопрос с анимацией движения в разных направлениях. рекомендую и Вам)
				if (animator)
				{
					if (animator.GetFloat("x") != value.x)
						animator.SetFloat("x", value.x);
					if (animator.GetFloat("y") != value.y)
						animator.SetFloat("y", value.y);
				}
			}
		}

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
				action != "dead"
					&&
				action != ConnectController.ACTION_REMOVE
					&&
				DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1
					&&
				(animator.GetCurrentAnimatorStateInfo(layerIndex).loop || animator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1.0f) 	
			)
			{
				string layer_name = animator.GetLayerName(layerIndex);
                if (layer_name != "idle")
                {
					Debug.LogWarning(DateTime.Now.Millisecond + " " + key + ": idle с " + action +" (таймаут)");
					Animate(animator, animator.GetLayerIndex("idle"));
				}
			}
		}

		/// <summary>
		/// этот метод для возможноости переопределения его же самого нужен но с другими типами аргументов
		/// </summary>
		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		/// <summary>
		/// переопределим метод срабатываемый при присвоениеии пришедших с сервера данных и начнем включать анимацию
		/// </summary>
		protected void SetData(NewObjectRecive recive)
		{
			// если мы двигаемся и пришли новые координаты - то сразу переместимся на локацию к которой идем
			string move_action = null;
			if (recive.x != null || recive.y != null || recive.z != null)
			{
				Vector3 new_position = new Vector3(recive.x ?? position.x, recive.y ?? position.y, recive.z ?? position.z);

				if ((recive.action == "walk" || recive.action == ConnectController.ACTION_REMOVE) && Vector3.Distance(position, new_position) < ConnectController.step * 1.5)
				{
					if (recive.action == ConnectController.ACTION_REMOVE)
					{
						Debug.LogError("Переход между локациями");
						move_action = "walk";
					}

					double timeout = getEvent(WalkResponse.GROUP).timeout ?? GetEventRemain(WalkResponse.GROUP);

					// в приоритете getEvent(WalkResponse.GROUP).timeout  тк мы у него не отнимаем время пинга на получение пакета но и не прибавляем ping время на отправку с сервера нового пакета
					coroutines["walk"] = StartCoroutine(Walk(new_position, (recive.action == ConnectController.ACTION_REMOVE ? timeout * 1.5 : timeout), (coroutines.ContainsKey("walk") ? coroutines["walk"] : null)));
				}
                else
				{
					if(transform.localPosition!=Vector3.zero)
						Debug.Log("Телепорт из "+ transform.localPosition + " в "+new_position);

					transform.localPosition = new_position;

					if (coroutines.ContainsKey("walk"))
						StopCoroutine("Walk");
				}
			}

			base.SetData(recive);

			// отложенное action. именно так - base.SetData  должен запустить отсчет что если карта не менется существо удаляется
			if (move_action != null)
				action = recive.action = move_action;

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			if (recive.action != null)
			{
				if (animator != null && recive.action != ConnectController.ACTION_REMOVE)
				{
					int layerIndex = animator.GetLayerIndex(recive.action);
					if (layerIndex == -1)
					{
						Debug.LogWarning("Положение без группы-слоя анимации ");
					}
					else
					{
						Debug.LogWarning(DateTime.Now.Millisecond + " " + key + ": " + recive.action + " с " + action);
						Animate(animator, layerIndex);
					}
				}
			}
		}

		/// <summary>
		/// включить анимацию - те отключить все слои анимаций других и оставить только нужную. если есть анмиационный тригер одноименный со слоем - и его выключить (для анимаций которых не зацикленные и надо запустить один раз)
		/// </summary>
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
		/// она вошла в плагин тк движение нужно в любой игре а координаты часть стандартного функционала, вы можете переопределить ее
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		protected virtual IEnumerator Walk(Vector3 finish, double timeout, Coroutine old_coroutine)
		{
			if (old_coroutine != null)
				StopCoroutine(old_coroutine);

			float distance;
			float distancePerUpdate = (float)(Vector3.Distance(transform.localPosition, finish) / (timeout / Time.fixedDeltaTime));

			float extropolation = ((float)ConnectController.Ping() / 2 + Time.fixedDeltaTime) / (float)getEvent(WalkResponse.GROUP).timeout * ConnectController.step;
			if (extropolation < distancePerUpdate) extropolation = distancePerUpdate;

			bool extropolation_start = false;

			while (((distance = Vector3.Distance(transform.localPosition, finish)) > 0 || (getEvent(WalkResponse.GROUP).action.Length > 0 && ConnectController.EXTROPOLATION)) && action=="walk")
			{
				// если уже подошли но с сервера пришла инфа что следом будет это же событие группы - экстрополируем движение дальше
				if (distance < distancePerUpdate)
				{
					// Здесь экстрополяция - на сервере игрок уже может и дошел но мы продолжаем двигаться если есть уже команды на следующее движение
					// не экстрополируем существ у которых нет lifeRadius а то они будут вечно куда то идти а сервер для них не отдаст новых данных
					if (action != ConnectController.ACTION_REMOVE && getEvent(WalkResponse.GROUP).action.Length > 0 && lifeRadius > 0 && ConnectController.EXTROPOLATION && Vector3.Distance(transform.localPosition, finish) < extropolation)
					{
						extropolation_start = true;

						// чуть снизим скорость
						finish += Vector3.Scale(new Vector3(forward.x, forward.y, finish.z).normalized, new Vector3(extropolation, extropolation, 1));
						Debug.LogError("Экстрополяция");
					}
					else
					{
						transform.localPosition = finish;
						break;
					}
				}
				else if (extropolation_start) break;

				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком
				activeLast = DateTime.Now;
				//Debug.LogError("Оставшееся время: "+GetEventRemain(WalkResponse.GROUP));

				transform.localPosition = Vector3.MoveTowards(transform.localPosition, finish, distancePerUpdate);
				yield return new WaitForFixedUpdate();
			}

			Debug.LogError(DateTime.Now.Millisecond + "  завершена корутина движения");

			coroutines.Remove("walk");
		}

		/// <summary>
		/// анимированное удаление объекта с карты (например когда снаряд попал в цель или игрок уходит с карты и др у кого есть анимация ACTION_REMOVE)
		/// </summary>
		protected override IEnumerator Destroy()
		{
			if (animator != null)
			{
				Debug.Log("Запуск анмаиции удаления с карты");

				Animate(animator, animator.GetLayerIndex(ConnectController.ACTION_REMOVE));
				yield return new WaitForSeconds(animator.GetCurrentAnimatorStateInfo(0).length - 0.01f);
			}

			Destroy(gameObject);
		}
	}
}
