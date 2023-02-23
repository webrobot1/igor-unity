using UnityEngine;
namespace MyFantasy
{
	public class NewPlayerModel : NewEnemyModel
	{
		private string login;
		public int hp;
		// ��� ����������� � �������� �������
		protected SpriteRenderer sprite = null;

		protected override void Awake()
		{
			sprite = GetComponent<SpriteRenderer>();
			base.Awake();
		}

        public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewPlayerRecive)recive);
		}			
		
		private void SetData(NewPlayerRecive recive)
		{
			if (recive.login != null)
				this.login = recive.login;

			// ���� �� ������ ������ ���������������. ����������� - ������ �����������
			if (recive.components != null)
            {
				if(recive.components.hp != null)
				{
					if (statModel.hp == 0 && recive.components.hp > 0)
					{
						hp = (int)recive.components.hp;
						sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 1f);
					}
					else if (recive.components.hp == 0)
					{
						sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0.5f);
					}
				}
			}

			base.SetData(recive);
		}	
	}
}
