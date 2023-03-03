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
		/// может изменится в процессе игры (переход на другую локацию)
		/// </summary>
		protected int map_id;

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

		private Dictionary<string, EventRecive> events = new Dictionary<string, EventRecive>();

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
				StartCoroutine(Remove(map_id));
				
			if (recive.action != null)
				this.action = recive.action;		
			
			if (recive.forward_x != null || recive.forward_y != null)
				this.transform.forward.Set(recive.forward_x ?? this.transform.forward.x, recive.forward_y ?? this.transform.forward.y, this.transform.forward.z);			
			
			if (this.key == null)
			{
				if(recive.x != null && recive.y != null && recive.z != null)
					transform.position = new Vector3((float)recive.x, (float)recive.y, (float)recive.z);
				this.key = this.gameObject.name;
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
			
			if (recive.forward_x != null || recive.forward_y!=null)
			{
				forward = new Vector3(recive.forward_x ?? forward.x, recive.forward_y ?? forward.y);
			}			

			if (recive.sort != null)
				this.sort = (int)recive.sort;

			if (recive.lifeRadius != null)
				this.lifeRadius = (int)recive.lifeRadius;

			if (recive.created != null)
				this.created = recive.created;

			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.map_id > 0)
				this.map_id = recive.map_id;

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
						events[kvp.Key].finish = DateTime.Now.AddSeconds((double)kvp.Value.remain - (ConnectController.Ping()/2));
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
					if(kvp.Value.is_client != null)
						events[kvp.Key].is_client = kvp.Value.is_client;

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
		/// вернет количество секунд которых осталось до времени когда событие может быть сработано (тк есть события что шлем мы , а есть что шлются сами) 
		/// </summary>
		public virtual double GetEventRemain(string group)
		{
			// вычтем из времени время на доставку пакета (половина пинга)
			return getEvent(group).finish.Subtract(DateTime.Now).TotalSeconds;
		}

		/// <summary>
		/// корутина которая удаляет тз игры объект (если такая команда пришла с сервера). можно переопределить что бы изменить время удаления (0.5 секунда по умолчанию)
		/// </summary>
		protected virtual IEnumerator Remove(int map_id)
		{
			DateTime start = DateTime.Now;
            while (DateTime.Compare(start.AddSeconds(5), DateTime.Now) >= 1)
            {
				// если спустя паузу мы все еще на той же карте - удалим объект (это сделано для плавного реконекта при переходе на карту ДРУГИМИ игроками)
				if (this.map_id != map_id)
					yield break;
					
				yield return new WaitForFixedUpdate();
			}

			Destroy(gameObject);
		}
	}
}
