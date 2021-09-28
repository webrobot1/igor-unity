using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
    protected int id;
    protected int map_id;
	public string action = "idle";

	protected Animator anim;
	private static Dictionary<string, bool> trigers;

	protected void Start()
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

	public void SetData(ObjectJson data)
	{
		if (data.action.Length > 0 && this.action != data.action && trigers.ContainsKey(data.action))
		{
			Debug.Log("��������� �������� " + data.action);
			this.action = data.action;
			anim.SetTrigger(action);
		}

		if (data.map_id > 0)
			this.map_id = data.map_id;

		if (data.position.Length > 0 && this.id == 0)
		{	
			transform.position = new Vector2(data.position[0], data.position[1]);
		}

		if (this.id == 0)
		{
			this.id = data.id;
		}

	}
}
