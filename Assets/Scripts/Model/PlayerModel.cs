
public class PlayerModel : EnemyModel
{
	private string login;

	public void SetData(PlayerJson data)
	{
		if (base.id == 0)
		{
			this.login = data.login;
		}
		base.SetData(data);
	}	
}
