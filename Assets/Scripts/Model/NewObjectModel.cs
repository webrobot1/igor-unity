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
		[NonSerialized]
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
				action != "dead"
					&&
				action != ConnectController.ACTION_REMOVE
					&&
				DateTime.Compare(activeLast, DateTime.Now) < 1
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


		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		protected void SetData(NewObjectRecive recive)
		{
			base.SetData(recive);

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
				activeLast = DateTime.Now.AddMilliseconds(300);
			}

			

			// если мы двигаемся и пришли новые координаты - то сразу переместимся на локацию к которой идем
			if (recive.x != null || recive.y != null || recive.z != null || recive.action == ConnectController.ACTION_REMOVE)
			{
				// остановим корутину движения
				if (moveCoroutine != null)
				{
					StopCoroutine(moveCoroutine);
				}

				// если у нас перемещение на другую карту то очень быстро перейдем на нее что бы небыло дергания когда загрузится наш персонад на ней (тк там моментальный телепорт если еще не дошли ,т.е. дергание)
				if ((recive.action == "walk" || recive.action == ConnectController.ACTION_REMOVE) && recive.map_id == null)
                {
					// в приоритете getEvent(WalkResponse.GROUP).timeout  тк мы у него не отнимаем время пинга на получение пакета но и не прибавляем ping время на отправку с сервера нового пакета
					moveCoroutine = StartCoroutine(Walk(position, (recive.action == ConnectController.ACTION_REMOVE ? 0.2f : (getEvent(WalkResponse.GROUP).timeout ?? GetEventRemain(WalkResponse.GROUP)))));
				}					
				else
					transform.position = position;
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
		private IEnumerator Walk(Vector3 position, double timeout)
		{
			float distance;

			// нужны только для замедления при экстрополяции
			MoveDataRecive data;
			float speed = 1;	

			// Здесь экстрополяция - на сервере игрок уже может и дошел но мы продолжаем двигаться (используется таймаут а не фактическое оставшееся время тк при большом пинге игрок будет скакать)
			float distancePerUpdate = (float)(Vector3.Distance(transform.position, position) / (timeout / Time.fixedDeltaTime));
			
			while ((distance = Vector3.Distance(transform.position, position)) > 0 || getEvent(WalkResponse.GROUP).action.Length > 0)
			{

				// если уже подошли но с сервера пришла инфа что следом будет это же событие группы - экстрополируем движение дальше
				if (distance < distancePerUpdate || action == "dead" || action == "hurt")
                {
					// не интерполируем существ у которых нет lifeRadius а то они будут вечно куда то идти а сервер для них не отдаст новых данных
					if (action != "dead" && action != "hurt" && action != ConnectController.ACTION_REMOVE && getEvent(WalkResponse.GROUP).action.Length > 0 && lifeRadius>0) 
					{
						distancePerUpdate *= 0.5f;

						switch (getEvent(WalkResponse.GROUP).action)
						{
							case "kamikadze":
								position += new Vector3(forward.x, forward.y, position.z);

								Debug.LogError("экстрополируем");
							break;						
							case "index":
								data = getEventData<MoveDataRecive>(WalkResponse.GROUP);

								position += new Vector3(data.x, data.y, position.z);

								Debug.LogError("экстрополируем");
							break;						
							case "to":
								// я немогу это экстрополировать тк незнаю в какоую сторону поиск пути сработает так что просто движемся в том же направлении и ждем сервер
								position +=  new Vector3(forward.x * distancePerUpdate, forward.y * distancePerUpdate, position.z * distancePerUpdate);
							break;
						}					
					}
                    else
                    {
						transform.position = position;
						break;
					}
				}

				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком
				activeLast = DateTime.Now.AddMilliseconds(300);

				transform.position = Vector3.MoveTowards(transform.position, position, distancePerUpdate);
				yield return new WaitForFixedUpdate();		
			}

			Debug.LogError(DateTime.Now.Millisecond + "  завершена корутина движения");
			moveCoroutine = null;
		}

		protected override IEnumerator Destroy()
		{
			Animate(animator, animator.GetLayerIndex(ConnectController.ACTION_REMOVE));
			yield return new WaitForSeconds(animator.GetCurrentAnimatorStateInfo(0).length - 0.01f);
			Destroy(gameObject);
		}
	}
}
