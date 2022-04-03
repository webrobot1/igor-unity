using UnityEngine;

public class PlayerModel : EnemyModel
{
	private string login;

	public void SetData(PlayerRecive data)
	{
		if (base.id == 0)
		{
			this.login = data.login;
		}

		// игрок всегда чуть ниже всех остальных по сортировке (те стоит всегда за объектами находящимися на его же координатах)
		if (data.position!=null)	
			data.position[1] += 0.01f;

		base.SetData(data);
	}	
}
