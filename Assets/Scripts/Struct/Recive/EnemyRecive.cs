/// <summary>
/// Структура полученных данных - враг
/// </summary>

[System.Serializable]
public class EnemyRecive : ObjectRecive
{
	// hp без знака ? всегда не пришедшее будет = 0 те будто надо дергать лайфба а с ним по умолчанию null и мы првоеряем именно пришло ли что то
	public int? hp;
	public int hpMax;

	public int? mp;	
	public int? mpMax;

	public float speed;
}