/// <summary>
/// ��������� ���������� ������ ��� �����������
/// </summary>

[System.Serializable]
public class SiginRecive
{
	public int id;
	public string token;

	/// <summary>
	/// ���������� �������� �� 1 unit (1 ����� ������� ����������)
	/// </summary>
	public float pixels;

	/// <summary>
	/// ���� ����� fixedDeltaTime (��� ����� FixedUpdate() ����������)
	/// </summary>
	public float time;
}
