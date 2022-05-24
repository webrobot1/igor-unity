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

	// срабатывает при взамодействии с объектом 
	private void OnTriggerStay2D(Collider2D other)
	{
		ObjectModel target;

		// если это наш игрок соприкоснулся
		// todo можно сделать что то если и не наш
		if(ConnectController.id == base.id) 
		{ 
			if (target = other.GetComponent<ObjectModel>())
			{

				Debug.Log(target.GetType().ToString());

				if (DateTime.Compare(target.LastTouch.AddMilliseconds(1000), DateTime.Now) < 1)
				{
					// проверим при каких условиях с объектами взаимодейстсовать НЕ нужно
					switch (target.prefab)
					{
						case "Altar":
							if (lifeBar.hp > 0)
								return;
							break;
					}

					Debug.LogWarning("соприкосновение с " + target.id);
					TouchResponse response = new TouchResponse();
					response.action = "objects";
					response.object_id = target.id;
					ConnectController.connect.Send(response);

					target.LastTouch = DateTime.Now;
				}
				else
					Debug.LogWarning("Уже соприкоснулись");
			}
			else
				Debug.LogError("Отсутвует ObjectModel  для обработки столкновений с " + other.name);
		}
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
