using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public LifeModel lifeBar;

	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public float distancePerUpdate;

	protected float speed;

	// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
	private DateTime activeLast = DateTime.Now;

	/// <summary>
	/// если не null - движемся
	/// </summary>
	private Coroutine? moveCoroutine;

    // Update is called once per frame
    void FixedUpdate()
	{
		// если мы не стоит и нет корутины что двигаемся и мы не жде ответа от сервера о движении (актуально лишь на нашего игрока)
		if (action.IndexOf("idle") == -1 && moveCoroutine == null && (base.anim.GetCurrentAnimatorStateInfo(0).loop || base.anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1)
		{
			Debug.LogWarning("останавливаем "+this.id);

			if (action.IndexOf("left")>-1)
				action = "idle_left";
			else if (action.IndexOf("right") > -1)
				action = "idle_right";
			else if (action.IndexOf("up") > -1)
				action = "idle_up";
			else
				action = "idle_down";

			base.anim.SetTrigger(action);
		}
	}

	public void SetData(EnemyRecive data)
	{
		activeLast = DateTime.Now;

        if (data.components != null) 
		{
			if (data.components.hp != null)
			{
				lifeBar.hp = (int)data.components.hp;
			}
			if (data.components.hpMax != null)
				lifeBar.hpMax = (int)data.components.hpMax;


			if (data.components.mp != null)
				lifeBar.mp = (int)data.components.mp;

			// ниже сравниваем c null тк может быть значение 0 которое надо обработать
			if (data.components.mpMax != null)
				lifeBar.mpMax = (int)data.components.mpMax;
		}


		// тут если speed=0 значит ничего не пришло
		if (data.speed > 0) 
		{ 
			this.speed = data.speed;

			// переделать.  
			distancePerUpdate = this.speed * Time.fixedDeltaTime;
		}


		if (this.id != 0 && (data.x !=null || data.y != null || data.z != null))
		{
			if (moveCoroutine != null)
				StopCoroutine(moveCoroutine);

			Vector3 moveTo = new Vector3((float)(data.x!=null?data.x:transform.position.x), (float)(data.y != null ? data.y : transform.position.y), (float)(data.z != null ? data.z : transform.position.z));

			// todo пока 1 шаг - 1 единици позиции  и если больше - это не ходьба а телепорт. в будушем может быть меньше 1 единицы
			// если отставание от текущей позиции больше чем полтора шага телепортнем (а може это и есть телепорт)
			if (Vector3.Distance(moveTo, transform.position) <= 1.5)
				moveCoroutine = StartCoroutine(Move(moveTo));
			else
				transform.position = moveTo;
		}

		base.SetData(data);
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

			transform.position = Vector3.MoveTowards(transform.position, position, (distance<distancePerUpdate? distance: distancePerUpdate));
			transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

			yield return new WaitForFixedUpdate();
		}

		activeLast = DateTime.Now;
		moveCoroutine = null;
	}
}
