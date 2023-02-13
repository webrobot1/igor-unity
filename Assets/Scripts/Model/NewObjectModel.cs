using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MyFantasy
{
	public class NewObjectModel : ObjectModel
	{
		protected Animator anim = null;
		protected static Dictionary<string, bool> trigers;

		/// <summary>
		/// ���� �� null - ��������
		/// </summary>
		private Coroutine moveCoroutine = null;


		protected virtual void Awake()
		{
			if (anim = GetComponent<Animator>())
			{
				// �������� ��� ��������� ������� �������� �, ���� ��� ������ action ��� ����� - ������� ��������
				if (trigers == null)
				{
					trigers = new Dictionary<string, bool>();
					foreach (var parameter in anim.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger))
					{
						trigers.Add(parameter.name, true);
					}
				}
			}
		}

		// Update is called once per frame
		void Update()
		{
			// ���� �� �� ����� � ��� �������� ��� ��������� � �� �� ��� ������ �� ������� � �������� (��������� ���� �� ������ ������)
			if (anim!=null && anim.GetCurrentAnimatorClipInfo(0)[0].clip.name.IndexOf("idle") == -1 && moveCoroutine == null && (anim.GetCurrentAnimatorStateInfo(0).loop || anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1.0f) && DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1)
			{
				Debug.LogWarning("������������� " + this.key);
				action = "idle_"+side;
				anim.SetTrigger(action);
			}
		}

		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		protected void SetData(NewObjectRecive recive)
		{
			// ���� �� ��������� � ��� �� ��� �������� (����� ������� ��� ����� � ��� �� ����� ��� ��������� ��������) �� ����� ������������ �� ������� � ������� ����
			if (moveCoroutine != null && (recive.action != null && recive.action!=action) || (recive.side != null && recive.side!=side))
			{
				StopCoroutine(moveCoroutine);
				transform.position = position;
			}

			base.SetData(recive);

			if ((recive.x != null || recive.y != null || recive.z != null) && position != transform.position)
			{
				if (recive.action == "move")
					moveCoroutine = StartCoroutine(Move(position));
				else
					transform.position = position;
			}

			string trigger;
			// ����������� ������ - �������� �������� ������ �� ��������� ������ ��������� � ��� ��������
			if (recive.action != null || recive.side != null )
			{
				trigger = action + "_" + side;
				if(anim!=null)
                {
					if (trigers.ContainsKey(trigger)) 
					{ 
						Debug.Log("��������� �������� " + trigger);
						anim.SetTrigger(trigger);
					}
					else
					{
						Debug.LogWarning("��������� ��� �������� " + trigger);
						trigger = "idle_" + side;
						anim.SetTrigger(trigger);
					}
				}
			}
		}


		/// <summary>
		/// �������� �������� NPC ��� ������. �������� ����� ������� ����� ����� ���������
		/// </summary>
		/// <param name="position">���� �������</param>
		private IEnumerator Move(Vector3 position)
		{
			float distance;

			float distancePerUpdate = (float)getEvent("move").timeout / Time.fixedDeltaTime;
			while ((distance = Vector3.Distance(transform.position, position)) > 0)
			{
				// ���� �������� ������ ������ ���  �� �������� �� FixedUpdate (������� ����) �� �������� ��� �������
				// � ���� ������ - ��������� � ������ �������� �������� �������

				transform.position = Vector3.MoveTowards(transform.position, position, (distance < distancePerUpdate ? distance : distancePerUpdate));
				transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

				Debug.Log(GetEventRemain("move"));
				yield return new WaitForFixedUpdate();
			}

			activeLast = DateTime.Now;
			moveCoroutine = null;
		}
	}
}
