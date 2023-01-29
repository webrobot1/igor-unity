using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
	protected int id;
	protected int map_id;
	protected string key;

	/// <summary>
	/// для того что бы менять сортировку при загрузке карты
	/// </summary>
	public int sort;

	protected DateTime created;
	protected string prefab;

	protected string action = "idle";

	protected Animator anim = null;
	private SpriteRenderer sprite = null;
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

		sprite = GetComponent<SpriteRenderer>();
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

		// пришла команды удаления с карты объекта
		if(data.action == "remove")
        {
			DestroyImmediate(gameObject);
			return;
		}

		if (data.map_id > 0)
			this.map_id = data.map_id;


		// сортировку не сменить в SetData тк я не хочу менять уровент изоляции spawn_sort
		if (data.sort > 0)
			this.sort = (int)data.sort;

		if (this.id == 0 && data.x!=null && data.y != null && data.z != null)
		{	
			transform.position = new Vector3((float)data.x, (float)data.y, (float)data.z);
		}

		if (this.id == 0)
		{
			this.id = data.id;
			this.key = data.key;
			this.created = data.created;
			this.prefab = data.prefab;
		}
	}
}
