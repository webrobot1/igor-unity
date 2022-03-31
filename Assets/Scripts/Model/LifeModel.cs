using UnityEngine;
using UnityEngine.UI;

public class LifeModel : MonoBehaviour
{
	[SerializeField]
	private Image maxHurt;
	private int _hpMax;
	public int hpMax
	{
		get { return _hpMax; }

		set
		{
			Debug.Log(maxHurt.rectTransform.right);
			maxHurt.rectTransform.right = new Vector3(-2,0,0);
			_hpMax = value;
		}
	}


	[SerializeField]
	private Image currentHurt;
	private int _hp;
	public int hp
	{
		get { return _hp; } 

		set {
			//GameObject hurt = Instantiate(Resources.Load("Prefabs/Hurt", typeof(GameObject))) as GameObject;
			//hurt.transform.position = Vector3.zero;
			//	hurt.transform.parent = this.transform;


			_hp = value;
		}  
	}

	public int mpMax = 20;
	public int mp;


}