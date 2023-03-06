using UnityEngine;
namespace MyFantasy
{
	public class NewPlayerModel : NewEnemyModel
	{

		// для превращения в призрака игроков
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

		protected override void Dead()
		{
			sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 0.5f); 
			base.Dead();
		}

		protected override void Resurrect()
		{
			sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, 1f);
			base.Resurrect();
		}

		private void SetData(NewPlayerRecive recive)
		{

			base.SetData(recive);
		}	
	}
}
