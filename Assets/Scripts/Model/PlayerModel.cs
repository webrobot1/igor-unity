using UnityEngine;

public class PlayerModel : EnemyModel
{
	private string login;

	/// <summary>
	/// ���������� �� ����� � ������� �� �������� �� ����� ���� (��� ��� ���� �� ������ �������������� ���������)
	/// </summary>
	public Vector2 moveTo = Vector2.zero;

	public void SetData(PlayerRecive data)
	{
		if (base.id == 0)
		{
			this.login = data.login;
		}

		// ���� �� �������������� ������ ������������� �� � ��� ���������� �������� � ���� ����������
		if (data.action == "idle")
		{
			this.moveTo = Vector2.zero;
		}

		// ����� ������ ���� ���� ���� ��������� �� ���������� (�� ����� ������ �� ��������� ������������ �� ��� �� �����������)
		if (data.position!=null)	
			data.position[1] += 0.01f;

		base.SetData(data);
	}	
}
