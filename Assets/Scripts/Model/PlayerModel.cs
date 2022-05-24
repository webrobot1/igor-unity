using System;
using UnityEngine;

public class PlayerModel : EnemyModel
{
	private string login;
	private SpriteRenderer render;

	protected void Awake()
	{
		base.Awake();
		render = GetComponent<SpriteRenderer>();
	}

	public void SetData(PlayerRecive data)
	{
		if (base.id == 0)
		{
			this.login = data.login;
		}

		// игрок всегда чуть ниже всех остальных по сортировке (те стоит всегда за объектами находящимися на его же координатах)
		if (data.position!=null)	
			data.position[1] += 0.01f;

		// преращение в празирака
		if (data.hp != null)
		{
			if (lifeBar.hp == 0 && data.hp > 0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 1f); // (r,g,b,a); последний параметр прозрачность. От 0 до 1.
			}
			else if (data.hp == 0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 0.5f);
			}
		}

		base.SetData(data);
	}	
}
