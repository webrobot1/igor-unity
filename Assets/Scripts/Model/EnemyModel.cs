using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	public float speed;
	protected int hp;
	protected int mp;

	// когда был последний пинг
	private DateTime _pingLast = DateTime.Now;

	/// <summary>
	/// если не null - движемся
	/// </summary>
	private Coroutine? moveCoroutine;


	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public float distancePerUpdate;   	

	new void Start()
	{
		base.Start();
	}

	// Update is called once per frame
	void FixedUpdate()
	{
		// если мы не стоит и нет корутины что двигаемся и мы не жде ответа от сервера о движении (актуально лишь на нашего игрока)
		if (action != "idle" && moveCoroutine == null && DateTime.Compare(_pingLast.AddMilliseconds(1000), DateTime.Now) < 1)
		{
			Debug.LogError("останавливаемы");
			action = "idle";
			base.anim.SetTrigger(action);
		}
	}

	public void SetData(EnemyJson data)
	{
		_pingLast = DateTime.Now;

		if (data.hp > 0)
			this.hp = data.hp;

		if (data.mp > 0)
			this.mp = data.mp;

		if (data.speed > 0) { 
			this.speed = data.speed;
			distancePerUpdate = this.speed * Time.fixedDeltaTime;
		}

		if (this.id != 0 && data.position.Length>0)
		{
			if (moveCoroutine != null)
				StopCoroutine(moveCoroutine);

			moveCoroutine = StartCoroutine(Move(new Vector2(data.position[0], data.position[1])));
		}
		
		base.SetData(data);
	}

	// todo придумать как перенести это в main Controller (тк пауза на передвижения актуальна лишь у управляемого игрока а не у всех)

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
		moveCoroutine = null;
	}
}
