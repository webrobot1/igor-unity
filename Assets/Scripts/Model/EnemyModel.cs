using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	protected float speed;
	protected int hp;
	protected int mp;

	// ����� ��������� ��� ��������� ������ (��� ���������� action - idle �� ��������)
	private DateTime activeLast = DateTime.Now;

	/// <summary>
	/// ���� �� null - ��������
	/// </summary>
	private Coroutine? moveCoroutine;


	/// <summary>
	/// ���������� ��������� �� FixedUpdate (����������� �������� ������)
	/// </summary>
	public float distancePerUpdate;   	


	// Update is called once per frame
	void FixedUpdate()
	{
		// ���� �� �� ����� � ��� �������� ��� ��������� � �� �� ��� ������ �� ������� � �������� (��������� ���� �� ������ ������)
		if (action.IndexOf("idle") == -1 && moveCoroutine == null && (base.anim.GetCurrentAnimatorStateInfo(0).loop || base.anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(200), DateTime.Now) < 1)
		{
			Debug.LogError("������������� "+this.id);

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

		if (data.hp > 0)
			this.hp = data.hp;

		if (data.mp > 0)
			this.mp = data.mp;

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

	// todo ��������� ��� ��������� ��� � main Controller (�� ����� �� ������������ ��������� ���� � ������������ ������ � �� � ����)

	/// <summary>
	/// �������� �������� NPC ��� ������ � ����� � ������ �-�� �� ��������
	/// </summary>
	/// <param name="position">���� �������</param>
	private IEnumerator Move(Vector2 position)
	{
		float distance;

		while ((distance = Vector2.Distance(transform.position, position)) > 0)
		{
			// ���� �������� ������ ������ ���  �� �������� �� FixedUpdate (������� ����) �� �������� ��� �������
			// � ���� ������ - ��������� � ������ �������� �������� �������

			transform.position = Vector2.MoveTowards(transform.position, position, (distance<distancePerUpdate? distance: distancePerUpdate));
			yield return new WaitForFixedUpdate();
		}

		activeLast = DateTime.Now;
		moveCoroutine = null;
	}
}
