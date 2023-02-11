using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
	public class StatModel : MonoBehaviour
	{
		NewObjectModel objectModel;

		void Awake()
		{
			objectModel = GetComponent<NewObjectModel>();
		}

		public RectTransform maxHurt;
		private float _hpMax;

		public float hpMax
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
		public float hp
		{
			get { return _hp; } 

			set {
				currentHurt.offsetMax = new Vector2(value/2f, maxHurt.offsetMax.y);
				_hp = value;
			}  
		}
		private float _hp;	
	
	
		public RectTransform maxMana;
		private float _mpMax;
		public float mpMax
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
		public float mp
		{
			get { return _mp; } 

			set {
				currentMana.offsetMax = new Vector2(value/2f, maxMana.offsetMax.y);
				_mp = value;
			}  
		}
		private float _mp;

		// для превращения в призрака игроков
		[SerializeField]
		protected float lineSpeed = 2;


		protected void Update()
		{
			if (objectModel.key == PlayerController.Instance.player_key)
			{
				if (PlayerController.Instance.hpFrame.fillAmount != hp / hpMax)
				{
					PlayerController.Instance.hpFrame.fillAmount = Mathf.Lerp(PlayerController.Instance.hpFrame.fillAmount, hp /hpMax, Time.deltaTime * lineSpeed);
					PlayerController.Instance.hpFrame.GetComponentInChildren<Text>().text = hp+" / "+hpMax;
				}

				if (PlayerController.Instance.mpFrame.fillAmount != mp / mpMax)
				{
					PlayerController.Instance.mpFrame.fillAmount = Mathf.Lerp(PlayerController.Instance.mpFrame.fillAmount, mp / mpMax, Time.deltaTime * lineSpeed);
					PlayerController.Instance.mpFrame.GetComponentInChildren<Text>().text = mp + " / " + mpMax;
				}
			}
		}

	}
}