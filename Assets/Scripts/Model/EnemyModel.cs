using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	public float speed;
	protected int hp;
	protected int mp;

	// ����� ��� ��������� ����
	private DateTime _pingLast = DateTime.Now;

	/// <summary>
	/// ���� �� null - ��������
	/// </summary>
	private Coroutine? moveCoroutine;


	/// <summary>
	/// ���������� ��������� �� FixedUpdate (����������� �������� ������)
	/// </summary>
	public float distancePerUpdate;   	

	new void Start()
	{
		base.Start();
	}

	// Update is called once per frame
	void FixedUpdate()
	{
		// ���� �� �� ����� � ��� �������� ��� ��������� � �� �� ��� ������ �� ������� � �������� (��������� ���� �� ������ ������)
		if (action != "idle" && moveCoroutine == null && DateTime.Compare(_pingLast.AddMilliseconds(1000), DateTime.Now) < 1)
		{
			Debug.LogError("��������������");
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

	// todo ��������� ��� ��������� ��� � main Controller (�� ����� �� ������������ ��������� ���� � ������������ ������ � �� � ����)

	/// <summary>
	/// �������� �������� NPC ��� ������ � ����� � ������ �-�� �� ��������
	/// </summary>
	/// <param name="position">���� �������</param>
	private IEnumerator Move(Vector2 position)
	{
		while (Vector2.Distance(transform.position, position) >= distancePerUpdate)
		{
			transform.position = Vector2.MoveTowards(transform.position, position, distancePerUpdate);
			yield return new WaitForFixedUpdate();
		}
		moveCoroutine = null;
	}
}
