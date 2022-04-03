using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	protected float speed;

	// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
	private DateTime activeLast = DateTime.Now;

	/// <summary>
	/// если не null - движемся
	/// </summary>
	private Coroutine? moveCoroutine;

	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public float distancePerUpdate;

	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public LifeModel lifeBar;

	private SpriteRenderer render;

	protected void Awake()
	{
		base.Awake();
		render = GetComponent<SpriteRenderer>();
	}

    // Update is called once per frame
    void FixedUpdate()
	{
		// если мы не стоит и нет корутины что двигаемся и мы не жде ответа от сервера о движении (актуально лишь на нашего игрока)
		if (action.IndexOf("idle") == -1 && moveCoroutine == null && (base.anim.GetCurrentAnimatorStateInfo(0).loop || base.anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(200), DateTime.Now) < 1)
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

		if (data.hpMax>0)
			lifeBar.hpMax = data.hpMax;

		// ниже сравниваем c null тк может быть значение 0 которое надо обработать
		if (data.mpMax > 0)
			lifeBar.mpMax = (int)data.mpMax;

		if(data.hp !=null)
		{ 
			if (lifeBar.hp == 0 && data.hp > 0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 1f); // (r,g,b,a); последний параметр прозрачность. От 0 до 1.
			}
			else if (data.hp==0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 0.5f);
			}

			lifeBar.hp = (int)data.hp;
		}

		if (data.mp !=null)
			lifeBar.mp = (int)data.mp;

		// тут если speed=0 значит ничего не пришло
		if (data.speed > 0) { 
			this.speed = data.speed;
			distancePerUpdate = this.speed * Time.fixedDeltaTime;
		}

		if (this.id != 0 && data.position!=null && data.position.Length>0)
		{
			if (moveCoroutine != null)
				StopCoroutine(moveCoroutine);

			moveCoroutine = StartCoroutine(Move(new Vector2(data.position[0], data.position[1])));
		}

		base.SetData(data);
	}

	/// <summary>
	/// анимация движения NPC или игрока к точки с учетом х-ки их скорости
	/// </summary>
	/// <param name="position">куда движемя</param>
	private IEnumerator Move(Vector2 position)
	{
		float distance;

		while ((distance = Vector2.Distance(transform.position, position)) > 0)
		{
			// если остальсь пройти меньше чем  мы проходим за FixedUpdate (условно кадр) то движимся это отрезок
			// в ином случае - дистанцию с учетом скорости проходим целиком

			transform.position = Vector2.MoveTowards(transform.position, position, (distance<distancePerUpdate? distance: distancePerUpdate));
			yield return new WaitForFixedUpdate();
		}

		activeLast = DateTime.Now;
		moveCoroutine = null;
	}
}
