using UnityEngine;
namespace Mmogick
{
	public class PlayerModel : EnemyModel
	{
        public override void SetData(EntityRecive recive)
		{
			PrepareComponents(((PlayerRecive)recive).components);
			this.SetData((PlayerRecive)recive);
		}

		// Применяет альфу ко ВСЕМ SpriteRenderer'ам под сущностью.
		// SpriteRenderer живёт в child "Sprites" (UpdateController переносит туда fallback при спавне,
		// Spriter — ставит туда же N child-спрайтов). Кеш в Awake недопустим: wrap/Spriter могут пересоздать
		// структуру в любой момент — берём актуальные каждый раз.
		private void SetSpritesAlpha(float alpha)
		{
			foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
			{
				var c = sr.color;
				sr.color = new Color(c.r, c.g, c.b, alpha);
			}
		}

		protected override void Dead()
		{
			SetSpritesAlpha(0.5f);
			base.Dead();
		}

		protected override void Resurrect()
		{
			SetSpritesAlpha(1f);
			base.Resurrect();
		}

		private void SetData(PlayerRecive recive)
		{
			base.SetData(recive);
		}
	}
}
