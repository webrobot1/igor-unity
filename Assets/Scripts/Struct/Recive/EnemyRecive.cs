/// <summary>
/// Структура полученных данных - враг
/// </summary>

[System.Serializable]
public class EnemyRecive : ObjectRecive
{
	public int hp;
	public int hpMax;

	public int mp;	
	public int mpMax;

	public float speed;
}