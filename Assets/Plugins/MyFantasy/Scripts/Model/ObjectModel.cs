using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MyFantasy
{
	public class ObjectModel : MonoBehaviour
	{
		/// <summary>
		/// индентификатор сущности
		/// </summary>
		public string key;

		/// <summary>
		/// для того что бы менять сортировку при загрузке карты
		/// </summary>
		public int sort;

		/// <summary>
		/// может изменится в процессе игры (переход на другую локацию)
		/// </summary>
		protected int map_id;

		protected DateTime created;
		protected string prefab;

		protected string action = "idle";
		public string side = "down";

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		public DateTime activeLast = DateTime.Now;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		private Dictionary<string, EventRecive> events = new Dictionary<string, EventRecive>();
		private List<float> pings = new List<float>();

		public virtual void SetData(ObjectRecive recive)
		{
			activeLast = DateTime.Now;

			// пришла команды удаления с карты объекта
			if (recive.action == "remove/index")
			{
				DestroyImmediate(gameObject);
				return;
			}

			if (recive.action != null && recive.action.Length > 0)
				this.action = recive.action;		
			
			if (recive.side != null && recive.side.Length > 0)
				this.side = recive.side;

			// сортировку не сменить в SetData тк я не хочу менять уровент изоляции spawn_sort
				if (recive.sort > 0)
				this.sort = (int)recive.sort;

			if (this.key.Length == 0 && recive.x != null && recive.y != null && recive.z != null)
			{
				transform.position = new Vector3((float)recive.x, (float)recive.y, (float)recive.z);
			}

			if (this.key.Length > 0 && (recive.x != null || recive.y != null || recive.z != null))
			{
				transform.position = new Vector3((float)(recive.x != null ? recive.x : transform.position.x), (float)(recive.y != null ? recive.y : transform.position.y), (float)(recive.z != null ? recive.z : transform.position.z));
			}

			// тут если speed=0 значит ничего не пришло
			if (recive.speed > 0)
			{
				this.speed = recive.speed;
			}

			if (recive.created != null)
				this.created = recive.created;

			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.map_id > 0)
				this.map_id = recive.map_id;

			if (this.key.Length == 0)
				this.key = this.gameObject.name;

			if (recive.events!=null && recive.events.Count > 0)
			{
				Debug.Log("Обновляем таймауты событий");
				foreach (KeyValuePair<string, EventRecive> kvp in recive.events)
				{
					if (!events.ContainsKey(kvp.Key))
						events.Add(kvp.Key, kvp.Value);
                    else
					{
						if (kvp.Value.command_id > 0)
						{
							if (pings.Count > 10)
							{
								pings.RemoveRange(0, 5);
							}

							pings.Add((float)((new DateTimeOffset(DateTime.Now)).ToUnixTimeMilliseconds() - kvp.Value.command_id) / 1000 - kvp.Value.wait_time);
						}

						if (kvp.Value.remain != null) 
						{ 
							// мы отномаем от оставшегося времени до исполнения половину пинга как то время которое было потрачено пока ответ шел с сервера к нам
							events[kvp.Key].finish = DateTime.Now.AddSeconds((float)kvp.Value.remain - (Ping() / 2));
						}

						if (kvp.Value.action!="")
							events[kvp.Key].action = kvp.Value.action;
					}
				}
			}
		}

		public double GetTimeout(string group)
		{
			// если на нас нет данного события то значит можно его вполнять на сервере
			if (!events.ContainsKey(group))
			{
				return 0;
			}
            else 
				return ((DateTime)events[group].finish).Subtract(DateTime.Now).TotalSeconds;
		}

		/// <summary>
		/// для установки таймаута по последним известным вводным (с сервера придут данные для точного значения)
		/// </summary>
		public void SetTimeout(string group)
		{
			if (!events.ContainsKey(group))
			{
				events.Add(group, new EventRecive());
			}

			float remain;

			// если таймаутов нет установим дефолтные значения
			if (events[group].remain == null)
				remain = ConnectController.timeouts[group];
			else
				remain = (float)events[group].remain;

			// если мы только вошли в игру, у нас нет текущего события на нас и мы не знаем сколько там таймаут и еще не пришел ответ со значениями - установим по дефолту (он придет скоро)
			events[group].finish = DateTime.Now.AddSeconds(remain);
		}

		public double Ping()
		{
			return (pings.Count > 0 ? Math.Round((pings.Sum() / pings.Count), 3) : 0);
		}
	}
}
