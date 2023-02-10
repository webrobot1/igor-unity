using System;
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

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		protected DateTime activeLast = DateTime.Now;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		/// <summary>
		/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
		/// </summary>
		public float distancePerUpdate;

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

				// todo переделать основываясь на таймаутах событий
				distancePerUpdate = this.speed * Time.fixedDeltaTime;
			}

			if (recive.created != null)
				this.created = recive.created;

			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.map_id > 0)
				this.map_id = recive.map_id;

			if (this.key.Length == 0)
				this.key = this.gameObject.name;
		}
	}
}
