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
	private int _hp;
	public int hp
	{
		get { return _hp; } 

		set {
			currentHurt.offsetMax = new Vector2(value/2f, maxHurt.offsetMax.y);
			_hp = value;
		}  
	}

	public int? mpMax;
	public int? mp;


}