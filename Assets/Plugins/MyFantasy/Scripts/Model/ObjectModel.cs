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

		protected Animator anim = null;
		protected SpriteRenderer sprite = null;

		private static Dictionary<string, bool> trigers;

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		private DateTime activeLast = DateTime.Now;

		/// <summary>
		/// если не null - движемся
		/// </summary>
		private Coroutine moveCoroutine = null;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

		/// <summary>
		/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
		/// </summary>
		public float distancePerUpdate;

		protected void Awake()
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

			sprite = GetComponent<SpriteRenderer>();
		}

		// Update is called once per frame
		void FixedUpdate()
		{
			// если мы не стоит и нет корутины что двигаемся и мы не жде ответа от сервера о движении (актуально лишь на нашего игрока)
			if (action.IndexOf("idle") == -1 && moveCoroutine == null && (anim.GetCurrentAnimatorStateInfo(0).loop || anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1)
			{
				Debug.LogWarning("останавливаем " + this.key);

				if (action.IndexOf("left") > -1)
					action = "idle_left";
				else if (action.IndexOf("right") > -1)
					action = "idle_right";
				else if (action.IndexOf("up") > -1)
					action = "idle_up";
				else
					action = "idle_down";

				anim.SetTrigger(action);
			}
		}

		public virtual void SetData(ObjectRecive recive) 
		{
			activeLast = DateTime.Now;

			if (recive.action!=null && recive.action.Length > 0 && this.action != recive.action && trigers.ContainsKey(recive.action))
			{
				Debug.Log("Обновляем анимацию " + recive.action);
				this.action = recive.action;
				if(anim !=null)
					anim.SetTrigger(action);
			}

			// пришла команды удаления с карты объекта
			if(recive.action == "remove/index")
			{ 		
				DestroyImmediate(gameObject);
				return;
			}

			// сортировку не сменить в SetData тк я не хочу менять уровент изоляции spawn_sort
			if (recive.sort > 0)
				this.sort = (int)recive.sort;

			if (this.key.Length == 0 && recive.x!=null && recive.y != null && recive.z != null)
			{	
				transform.position = new Vector3((float)recive.x, (float)recive.y, (float)recive.z);
			}

			if (this.key.Length > 0 && (recive.x != null || recive.y != null || recive.z != null))
			{
				if (moveCoroutine != null)
					StopCoroutine(moveCoroutine);

				Vector3 moveTo = new Vector3((float)(recive.x != null ? recive.x : transform.position.x), (float)(recive.y != null ? recive.y : transform.position.y), (float)(recive.z != null ? recive.z : transform.position.z));

				// todo пока 1 шаг - 1 единици позиции  и если больше - это не ходьба а телепорт. в будушем может быть меньше 1 единицы
				// если отставание от текущей позиции больше чем полтора шага телепортнем (а може это и есть телепорт)
				if (Vector3.Distance(moveTo, transform.position) <= 1.5)
					moveCoroutine = StartCoroutine(Move(moveTo));
				else
					transform.position = moveTo;
			}

			// тут если speed=0 значит ничего не пришло
			if (recive.speed > 0)
			{
				this.speed = recive.speed;

				// todo переделать основываясь на таймаутах событий
				distancePerUpdate = this.speed * Time.fixedDeltaTime;
			}

			if (recive.created !=null)
				this.created = recive.created;	
		
			if (recive.prefab != null)
				this.prefab = recive.prefab;

			if (recive.map_id > 0)
				this.map_id = recive.map_id;

			if(this.key.Length==0)
				this.key = this.gameObject.name;
		}

		/// <summary>
		/// анимация движения NPC или игрока. скорость равна времени паузы между командами
		/// </summary>
		/// <param name="position">куда движемя</param>
		private IEnumerator Move(Vector3 position)
		{
			float distance;

			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
				// в ином случае - дистанцию с учетом скорости проходим целиком

				transform.position = Vector3.MoveTowards(transform.position, position, (distance < distancePerUpdate ? distance : distancePerUpdate));
				transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

				yield return new WaitForFixedUpdate();
			}

			activeLast = DateTime.Now;
			moveCoroutine = null;
		}
	}
}
