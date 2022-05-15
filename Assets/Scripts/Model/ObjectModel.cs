using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
	public int id;

	protected string action = "idle";
	protected int map_id;
	protected Animator anim = null;
	private static Dictionary<string, bool> trigers;

	protected void Awake()
	{
		if(anim = GetComponent<Animator>()) 
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
	}

	public void SetData(ObjectRecive data)
	{
		if (data.action!=null && data.action.Length > 0 && this.action != data.action && trigers.ContainsKey(data.action))
		{
			Debug.Log("Обновляем анимацию " + data.action);
			this.action = data.action;
			if(anim !=null)
				anim.SetTrigger(action);
		}

		if (data.map_id > 0)
			this.map_id = data.map_id;

		if (data.position != null && data.position.Length > 0 && this.id == 0)
		{	
			transform.position = new Vector2(data.position[0], data.position[1]);
		}

		if (this.id == 0)
		{
			this.id = data.id;
		}
	}
}
