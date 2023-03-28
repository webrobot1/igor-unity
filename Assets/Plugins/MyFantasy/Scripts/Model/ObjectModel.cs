using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MyFantasy
{
	public class ObjectModel : MonoBehaviour
	{
		/// <summary>
		/// для того что бы менять сортировку при загрузке карты
		/// </summary>
		[NonSerialized]
		public int sort;

		[NonSerialized]
		public int lifeRadius;

		/// <summary>
		/// индентификатор сущности
		/// </summary>
		[NonSerialized]
		public string key;		
		
		/// <summary>
		/// тип сущности
		/// </summary>
		[NonSerialized]
		public string type;

		/// <summary>
		/// может изменится в процессе игры (переход на другую локацию)
		/// </summary>
		protected int map_id;

		[NonSerialized]
		public string login;

		/// <summary>
		/// стандартное поле действия. хорошим тоном связать его с анимацией и в серверных механиках использовать
		/// </summary>
		[NonSerialized]
		public string action = "idle";


		protected DateTime created;
		protected string prefab;

		private Vector3 _forward = Vector3.zero;
		public virtual Vector3 forward
		{
			get { return _forward; }
			set
			{
				_forward = value;
			}
		}

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		protected DateTime activeLast = DateTime.Now;

		private Dictionary<string, EventRecive> events = new Dictionary<string, EventRecive>();
		protected Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

		/// <summary>
		/// координаты в которых  уже находится наш объект на сервере
		/// </summary>
		[NonSerialized]
		public Vector3 position = Vector3.zero;

		/// <summary>
		/// установка данных пришедших с сервера объекту 
		/// </summary>
		public virtual void SetData(ObjectRecive recive)
		{
			// пришла команды удаления с карты объекта
			if (recive.action == ConnectController.ACTION_REMOVE)
				StartCoroutine(Remove(map_id, (recive.map_id!=null)));
				
			if (recive.action != null)
            {
				this.action = recive.action;
				activeLast = DateTime.Now;
			}				
			
			if (recive.forward_x != null || recive.forward_y != null)
            {
				forward = new Vector3(recive.forward_x ?? forward.x, recive.forward_y ?? forward.y, this.transform.forward.z);

				this.transform.forward.Set(forward.x, forward.y, forward.z);

				// следующий код применим только к объектам - предметам, он повернет их
				if (type == "objects") 
				{ 
					float angle = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg * -1;
					transform.rotation = Quaternion.Euler(0, 0, angle);
				}
			}

			if (recive.x != null)
			{
				position.x = (float)recive.x;
			}

			if (recive.y != null)
			{
				position.y = (float)recive.y;
			}

			if (recive.z != null)
			{
				position.z = (float)recive.z;
			}


			// если мы двигаемся и пришли новые координаты - то сразу переместимся на локацию к которой идем
			if (recive.x != null || recive.y != null || recive.z != null || recive.action == ConnectController.ACTION_REMOVE)
			{
				// если у нас перемещение на другую карту то очень быстро перейдем на нее что бы небыло дергания когда загрузится наш персонад на ней (тк там моментальный телепорт если еще не дошли ,т.е. дергание)
				if ((recive.action == "walk" && recive.map_id == null) || recive.action == ConnectController.ACTION_REMOVE)
				{
					double timeout = getEvent(WalkResponse.GROUP).timeout ?? GetEventRemain(WalkResponse.GROUP);

					// в приоритете getEvent(WalkResponse.GROUP).timeout  тк мы у него не отнимаем время пинга на получение пакета но и не прибавляем ping время на отправку с сервера нового пакета
					coroutines["walk"] = StartCoroutine(Walk(position, (recive.action == ConnectController.ACTION_REMOVE ? timeout * 1.5 : timeout), (coroutines.ContainsKey("walk")?coroutines["walk"]:null)));
				}
				else
					transform.position = position;
			}
				
			if (this.key == null)
			{
				this.key = this.gameObject.name;
			}

			if (recive.sort != null)
				this.sort = (int)recive.sort;

			if (recive.lifeRadius != null)
				this.lifeRadius = (int)recive.lifeRadius;

			if (recive.created != null)
				this.created = recive.created;

			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.login != null)
				this.login = recive.login;

			if (recive.map_id !=null)
				this.map_id = (int)recive.map_id;

			if (recive.events!=null && recive.events.Count > 0)
			{
				foreach (KeyValuePair<string, EventRecive> kvp in recive.events)
				{
					if (!events.ContainsKey(kvp.Key))
						events.Add(kvp.Key, kvp.Value);

					// если мы сбрасяваем таймаут (например из каких то механик) - придет это поле (оно придет кстати и при таймауте события и может еще более точно скорректировать время таймаута)
					if (kvp.Value.remain != null) 
					{
						// вычтем время которое понадобилось что бы дойти ответу (половину пинга)
						events[kvp.Key].finish = DateTime.Now.AddSeconds((double)kvp.Value.remain - ConnectController.Ping() / 2);
					}				
					
					if (kvp.Value.timeout != null) 
					{ 
						events[kvp.Key].timeout = kvp.Value.timeout;
					}				
					
					if (kvp.Value.data != null) 
					{ 
						events[kvp.Key].data = kvp.Value.data;
					}

					// если false то сервер создал это событие. true по умолчанию 
					if (kvp.Value.from_client != null)
						events[kvp.Key].from_client = kvp.Value.from_client;

					if (kvp.Value.action != null) 
					{ 
						events[kvp.Key].action = kvp.Value.action;

						// если обнулилось событие то и обнуляются данные события (просто не высылаем что бы не тратить время)
						if(kvp.Value.action == "")
                        {
							events[kvp.Key].data = null;
						}
					}
				}
			}
		}

		/// <summary>
		/// получение данных события (без поля data)
		/// </summary>
		public virtual EventRecive getEvent(string group)
		{
			if (!events.ContainsKey(group))
			{
				events.Add(group, new EventRecive());
				events[group].action = "";
			}

			return events[group];
		}

		/// <summary>
		/// получения поля data события , нужно указвать какой cnnhernehs данных мы ожидаем будет это поле (по умолчанию это просто объект)
		/// </summary>
		public T getEventData<T>(string group) where T : new()
		{
			EventRecive ev = getEvent(group);
			return ev.data != null ? ev.data.ToObject<T>() : new T();
		}

		/// <summary>
		/// вернет количество секунд которых осталось до времени когда событие может быть сработано (тк есть события что шлем мы , а есть что шлются сами). из него уже был вычтено время затраченное на получение пакета с этим значением отсервера на сюда клиент (пол пинга) 
		/// если включена интерполяция при отправке команды будет еще вычтено пол пинга (время на доставку пакета команды на сервер ) для проверки можно ли уже слать запрос
		/// </summary>
		public virtual double GetEventRemain(string group)
		{
			// тут пинг не выитаем тк для анимации еще используется (она ведь должна продолжаться пока пакет идет).а если отправка команд идет в ConnectController - сверяясь вычитая пол пинга 
			return getEvent(group).finish.Subtract(DateTime.Now).TotalSeconds;
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// она вошла в плагин тк движение нужно в любой игре а координаты часть стандартного функционала, вы можете переопределить ее
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		protected virtual IEnumerator Walk(Vector3 position, double timeout, Coroutine old_coroutine)
		{
			if(old_coroutine!=null)
				StopCoroutine(old_coroutine);

			float distance;
			float distancePerUpdate = (float)(Vector3.Distance(transform.position, position) / (timeout / Time.fixedDeltaTime));

			Vector3 finish = position;

			float extropolation = ((float)ConnectController.Ping()/2+Time.fixedDeltaTime) / (float)getEvent(WalkResponse.GROUP).timeout * ConnectController.step;
			if (extropolation < distancePerUpdate) extropolation = distancePerUpdate;


			while ((distance = Vector3.Distance(transform.position, position)) > 0 || (getEvent(WalkResponse.GROUP).action.Length > 0 && ConnectController.EXTROPOLATION))
			{
				// если уже подошли но с сервера пришла инфа что следом будет это же событие группы - экстрополируем движение дальше
				if (distance < distancePerUpdate)
				{
					// Здесь экстрополяция - на сервере игрок уже может и дошел но мы продолжаем двигаться если есть уже команды на следующее движение
					// не экстрополируем существ у которых нет lifeRadius а то они будут вечно куда то идти а сервер для них не отдаст новых данных
					if (action != ConnectController.ACTION_REMOVE && getEvent(WalkResponse.GROUP).action.Length > 0 && lifeRadius > 0 && ConnectController.EXTROPOLATION && Vector3.Distance(transform.position, finish) < extropolation)
					{
						// чуть снизим скорость
						position += Vector3.Scale(new Vector3(forward.x, forward.y, position.z).normalized, new Vector3 (extropolation, extropolation, 1));
						Debug.LogError("Экстрополяция");
					}
					else
					{
						transform.position = position;
						break;
					}
				}

				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком
				activeLast = DateTime.Now;
				Debug.LogError("Оставшееся время: "+GetEventRemain(WalkResponse.GROUP));

				transform.position = Vector3.MoveTowards(transform.position, position, distancePerUpdate);
				yield return new WaitForFixedUpdate();
			}

			Debug.LogError(DateTime.Now.Millisecond + "  завершена корутина движения");

			coroutines.Remove("walk");
		}

		/// <summary>
		/// корутина которая удаляет тз игры объект (если такая команда пришла с сервера). можно переопределить что бы изменить время удаления (0.5 секунда по умолчанию)
		/// </summary>
		protected virtual IEnumerator Remove(int map_id, bool change_map = false)
		{
			DateTime start = DateTime.Now;
			if (change_map)
			{
				while (DateTime.Compare(start.AddSeconds(5), DateTime.Now) >= 1)
				{
					// если спустя паузу мы все еще на той же карте - удалим объект (это сделано для плавного реконекта при переходе на карту ДРУГИМИ игроками)
					if (this.map_id != map_id)
						yield break;

					yield return new WaitForFixedUpdate();
				}
			}
			StartCoroutine(this.Destroy());
		}

		/// <summary>
		///  базовая корутина уничтожение с карты объекта при уничтожении с сервера. ее можно и скорее нужно переопределять насыщая анмиацией это действи
		/// </summary>
		protected virtual IEnumerator Destroy()
		{
			Destroy(gameObject);
			yield return null;
		}
	}
}
