using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
    protected string action = "idle";
	
	protected int id;
	protected int map_id;
	protected Animator anim;
	private static Dictionary<string, bool> trigers;

	/// <summary>
	/// ���������� �� ����� � ������� �� �������� �� ����� ���� (��� ��� ���� �� ������ �������������� ���������)
	/// </summary>
	public Vector2 moveTo;

	protected void Awake()
	{
		anim = GetComponent<Animator>();

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

	public void SetData(ObjectRecive data)
	{
		if (data.action!=null && data.action.Length > 0 && this.action != data.action && trigers.ContainsKey(data.action))
		{
			Debug.Log("��������� �������� " + data.action);
			this.action = data.action;
			anim.SetTrigger(action);
		}

		// ���� �� �������������� ������ ������������� �� � ��� ���������� �������� � ���� ����������
		if (data.action == "idle")
		{
			this.moveTo = Vector2.zero;
		}

		if (data.map_id > 0)
			this.map_id = data.map_id;

		if (data.position != null && data.position.Length > 0 && this.id == 0)
		{	
			transform.position = new Vector3(data.position[0], data.position[1], transform.position.z);
		}

		if (this.id == 0)
		{
			this.id = data.id;
		}
	}
}
