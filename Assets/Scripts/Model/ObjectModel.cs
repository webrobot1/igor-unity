using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
	protected int id;
	protected string prefab;

	protected string action = "idle";
	protected int map_id;
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
			Destroy(gameObject);
			return;
		}

		if (data.map_id > 0)
			this.map_id = data.map_id;

		if (data.sort != null) { 
			sprite.sortingOrder = (int)data.sort + (int)ConnectController.spawn_sort;
			GetComponentInChildren<Canvas>().sortingOrder = (int)data.sort + (int)ConnectController.spawn_sort + 1;
		}

		if (data.position != null && data.position.Length > 0 && this.id == 0)
		{	
			transform.position = new Vector3(data.position[0], data.position[1], 1f);
		}

		if (this.id == 0)
		{
			this.id = data.id;
			this.prefab = data.prefab;
		}
	}
}
