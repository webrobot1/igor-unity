using UnityEngine;
namespace MyFantasy
{
	public class PlayerModel : EnemyModel
	{
		// для превращения в призрака игроков
		protected SpriteRenderer spriteRender = null;

		protected override void Awake()
		{
			spriteRender = GetComponent<SpriteRenderer>();
			base.Awake();
		}

        public override void SetData(EntityRecive recive)
		{
			PrepareComponents(((PlayerRecive)recive).components);
			this.SetData((PlayerRecive)recive);
		}

		protected override void Dead()
		{
			spriteRender.color = new Color(spriteRender.color.r, spriteRender.color.g, spriteRender.color.b, 0.5f); 
			base.Dead();
		}

		protected override void Resurrect()
		{
			spriteRender.color = new Color(spriteRender.color.r, spriteRender.color.g, spriteRender.color.b, 1f);
			base.Resurrect();
		}

		private void SetData(PlayerRecive recive)
		{
			base.SetData(recive);
		}	
	}
}
