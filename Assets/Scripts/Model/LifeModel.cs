using UnityEngine;
using UnityEngine.UI;

public class LifeModel : MonoBehaviour
{
	
	public RectTransform maxHurt;
	private int _hpMax;
	public int hpMax
	{
		get { return _hpMax; }

		set
		{
			maxHurt.offsetMax = new Vector2(value/2f, maxHurt.offsetMax.y);
			_hpMax = value;
		}
	}


	[SerializeField]
	private RectTransform currentHurt;
	public int hp
	{
		get { return _hp; } 

		set {
			currentHurt.offsetMax = new Vector2(value/2f, maxHurt.offsetMax.y);
			_hp = value;
		}  
	}
	private int _hp;	
	
	
	public RectTransform maxMana;
	private int _mpMax;
	public int mpMax
	{
		get { return _mpMax; }

		set
		{
			maxMana.offsetMax = new Vector2(value/2f, maxMana.offsetMax.y);
			_mpMax = value;
		}
	}


	[SerializeField]
	private RectTransform currentMana;
	public int mp
	{
		get { return _mp; } 

		set {
			currentMana.offsetMax = new Vector2(value/2f, maxMana.offsetMax.y);
			_mp = value;
		}  
	}
	private int _mp;

}