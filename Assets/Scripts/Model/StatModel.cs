using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
	public class StatModel : MonoBehaviour
	{
		NewObjectModel objectModel;
		CameraController cameraController;

		void Awake()
		{
			objectModel = GetComponent<NewObjectModel>();
			cameraController = Camera.main.GetComponent<CameraController>();
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
				if (Camera.main.GetComponent<CameraController>().hpFrame.fillAmount != fillAmount)
				{
					cameraController.hpFrame.fillAmount = Mathf.Lerp(cameraController.hpFrame.fillAmount, fillAmount, Time.deltaTime * lineSpeed);
					cameraController.hpFrame.GetComponentInChildren<Text>().text = hp+" / "+hpMax;
				}

				fillAmount = (float)mp / (float)mpMax;
				if (cameraController.mpFrame.fillAmount != mp / mpMax)
				{
					cameraController.mpFrame.fillAmount = Mathf.Lerp(cameraController.mpFrame.fillAmount, fillAmount, Time.deltaTime * lineSpeed);
					cameraController.mpFrame.GetComponentInChildren<Text>().text = mp + " / " + mpMax;
				}
			}
		}

	}
}