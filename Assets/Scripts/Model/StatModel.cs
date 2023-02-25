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

		/// <summary>
		///  скорость изменения полоски жизней и маны
		/// </summary>
		private float lineSpeed = 3;


		protected void Update()
		{
			if (objectModel.key == PlayerController.player_key)
			{
				float fillAmount = (float)hp / (float)hpMax;
				if (PlayerController.Instance.hpFrame.fillAmount != fillAmount)
				{
					PlayerController.Instance.hpFrame.fillAmount = Mathf.Lerp(PlayerController.Instance.hpFrame.fillAmount, fillAmount, Time.deltaTime * lineSpeed);
					PlayerController.Instance.hpFrame.GetComponentInChildren<Text>().text = hp+" / "+hpMax;
				}

				fillAmount = (float)mp / (float)mpMax;
				if (PlayerController.Instance.mpFrame.fillAmount != mp / mpMax)
				{
					PlayerController.Instance.mpFrame.fillAmount = Mathf.Lerp(PlayerController.Instance.mpFrame.fillAmount, fillAmount, Time.deltaTime * lineSpeed);
					PlayerController.Instance.mpFrame.GetComponentInChildren<Text>().text = mp + " / " + mpMax;
				}
			}
		}

	}
}