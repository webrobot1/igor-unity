using UnityEngine;

public class PlayerModel : EnemyModel
{
	private string login;

	/// <summary>
	/// координата на карте к которой мы движемся по клику мыши (или пок аким то другим принудительным действиям)
	/// </summary>
	public Vector2 moveTo = Vector2.zero;

	public void SetData(PlayerRecive data)
	{
		if (base.id == 0)
		{
			this.login = data.login;
		}

		// если мы олстанавливаем игрока принудительно то и все координаты двидения к цели обнуляются
		if (data.action == "idle")
		{
			this.moveTo = Vector2.zero;
		}

		// игрок всегда чуть ниже всех остальных по сортировке (те стоит всегда за объектами находящимися на его же координатах)
		if (data.position!=null)	
			data.position[1] += 0.01f;

		base.SetData(data);
	}	
}
