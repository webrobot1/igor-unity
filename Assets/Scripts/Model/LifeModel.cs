using UnityEngine;

public class LifeModel : MonoBehaviour
{
	public int hpMax;
	public int mpMax;

	public int hp
	{
		get { return hp; } 

		set { 
			GameObject hurt = Instantiate(Resources.Load("Prefabs/HurtFull", typeof(GameObject))) as GameObject;
			hurt.transform.parent = this.transform;
			hp = value;
		}  
	}

	
	public int mp;	
}