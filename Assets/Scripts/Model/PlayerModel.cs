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
		if (data.login!=null)
			this.login = data.login;

		// преращение в празирака
		if (data.components!=null && data.components.hp != null)
		{
			if (lifeBar.hp == 0 && data.components.hp > 0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 1f); // (r,g,b,a); последний параметр прозрачность. От 0 до 1.
			}
			else if (data.components.hp == 0)
			{
				render.color = new Color(render.color.r, render.color.g, render.color.b, 0.5f);
			}
		}

		base.SetData(data);
	}	
}
