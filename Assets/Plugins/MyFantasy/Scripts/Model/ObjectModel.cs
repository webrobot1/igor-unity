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
		[NonSerialized]
		public int map_id;

		[NonSerialized]
		public string login;

		/// <summary>
		/// стандартное поле действия. хорошим тоном связать его с анимацией и в серверных механиках использовать
		/// </summary>
		[NonSerialized]
		public string action = "idle";


		protected DateTime created;
		protected string prefab;

		private Vector2 _forward = Vector3.zero;

		/// <summary>
		/// при запросе поля выдает серверные значения. при смене - меняет transform position только в клиенте (на сервере меняется лишь попутно с другими событиями требующих направления)
		/// </summary>
		public virtual Vector3 forward
		{
			get { return _forward; }
			set
			{
				this.transform.forward.Set(value.x , value.y, value.z);
			}
		}

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		protected DateTime activeLast = DateTime.Now;

		private Dictionary<string, EventRecive> events = new Dictionary<string, EventRecive>();

		/// <summary>
		/// координаты в которых  уже находится наш объект на сервере (может не совпадать с позицией префаба тк анимация сглаживает скачки перехода и позиция изменяется постепенно в игре)
		/// </summary>
		[NonSerialized]
		public Vector3 position = Vector3.zero;

		/// <summary>
		/// установка данных пришедших с сервера объекту 
		/// </summary>
		public virtual void SetData(ObjectRecive recive)
		{
			if (recive.action != null)
			{
				this.action = recive.action;
				activeLast = DateTime.Now;

				// пришла команды удаления с карты объекта
				if (recive.action == ConnectController.ACTION_REMOVE && action != recive.action) 
				{ 
					StartCoroutine(this.Destroy());
				}
			}

			if (recive.map_id != null)
            {
				this.map_id = (int)recive.map_id;
            }
						

			if (recive.forward_x != null || recive.forward_y != null)
            {
				Vector3 vector = new Vector3(recive.forward_x ?? forward.x, recive.forward_y ?? forward.y);
				
				forward = vector;		// эта строка лишь развернет 
				_forward = vector;		// а это будет отдавать при запросе forward данные с сервера а не реального разворота в клиенте

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

			if (this.key == null)
			{
				this.key = this.gameObject.name;
			}

			if (recive.sort != null)
				this.sort = (int)recive.sort;

			if (recive.lifeRadius != null)
				this.lifeRadius = (int)recive.lifeRadius;

			if (recive.created != null)
				this.created = (DateTime)recive.created;

			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.login != null)
				this.login = recive.login;


			if (recive.events!=null && recive.events.Count > 0)
			{
				foreach (KeyValuePair<string, EventRecive> kvp in recive.events)
				{
					EventRecive ev = getEvent(kvp.Key);			

					// если мы сбрасяваем таймаут (например из каких то механик) - придет это поле (оно придет кстати и при таймауте события и может еще более точно скорректировать время таймаута)
					if (kvp.Value.remain != null) 
					{
						// вычтем время которое понадобилось что бы дойти ответу (половину пинга)
						ev.finish = DateTime.Now.AddSeconds((double)kvp.Value.remain - ConnectController.Ping() / 2);

						// новое врем событие - это косвенный признак что оно выполнено (хотя его время могло сбросить другие событие)
						// todo реализовать флаг на сервере
						ev.isFinish = true;
					}

					if (kvp.Value.timeout != null) 
					{
						ev.timeout = kvp.Value.timeout;
					}				
					
					if (kvp.Value.data != null) 
					{
						ev.data = kvp.Value.data;
					}

					// если false то сервер создал это событие. true по умолчанию 
					if (kvp.Value.from_client != null)
						ev.from_client = kvp.Value.from_client;

					if (kvp.Value.action != null) 
					{
						ev.action = kvp.Value.action;

						// если обнулилось событие то и обнуляются данные события (просто не высылаем что бы не тратить время)
						if(kvp.Value.action == "")
                        {
							ev.data = null;
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
				events[group].action = null;
				events[group].timeout = 0.5;
				events[group].from_client = true;
				events[group].finish = DateTime.Now;
				events[group].isFinish = true;
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
			return ((DateTime)getEvent(group).finish).Subtract(DateTime.Now).TotalSeconds;
		}

		public void Log(string message)
        {
			Debug.Log(name + ": "+ message);
		}

		public void LogWarning(string message)
        {
			Debug.LogWarning(name + ": "+ message);
		}
		public void LogError(string message)
        {
			Debug.LogError(name + ": "+ message);
		}

		/// <summary>
		///  базовая корутина уничтожение с карты объекта при уничтожении с сервера. ее можно и скорее нужно переопределять насыщая анмиацией это действи
		/// </summary>
		protected virtual IEnumerator Destroy()
		{
			Log("немедленное удаления с карты");
			Destroy(gameObject);

			yield return null;
		}
	}
}
