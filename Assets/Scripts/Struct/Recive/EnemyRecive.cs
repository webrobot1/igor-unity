/// <summary>
/// Структура полученных данных - враг
/// </summary>

[System.Serializable]
public class EnemyRecive : ObjectRecive
{
	public int hp;
	public int hpMax;

	public int? mp = 0;	
	public int? mpMax = 0;

	public float speed;
}