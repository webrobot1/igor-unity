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

		// ����� ������ ���� ���� ���� ��������� �� ���������� (�� ����� ������ �� ��������� ������������ �� ��� �� �����������)
		if (data.position!=null)	
			data.position[1] += 0.01f;

		base.SetData(data);
	}	
}
