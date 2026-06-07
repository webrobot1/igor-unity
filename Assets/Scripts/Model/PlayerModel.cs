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

		private void SetData(PlayerRecive recive)
		{
			base.SetData(recive);
		}

		// Применяет альфу ко ВСЕМ SpriteRenderer'ам под сущностью (включая Spriter-детей).
		// Кеш в Awake недопустим: wrap/Spriter могут пересоздать структуру в любой момент.
		private void SetSpritesAlpha(float alpha)
		{
			foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
			{
				var c = sr.color;
				sr.color = new Color(c.r, c.g, c.b, alpha);
			}
		}

		// Был ли в прошлом кадре «призрачный» режим — для однократного возврата непрозрачности на выходе.
		private bool _ghost;

		// «Призрачный» режим: hp=0 и action != dead (труп лежит при action=dead с полной альфой,
		// а двигается призраком — полупрозрачно). Условие пересчитывается каждый кадр в LateUpdate,
		// поэтому переключения action ↔ dead/walk/idle подхватываются автоматически без отдельных
		// триггеров от SetData. SpriterDotNet.UnityAnimator каждый кадр в ApplySpriteTransform
		// перезаписывает SpriteRenderer.color по info.Alpha из SCML — поэтому однократный SetAlpha
		// затирается на следующем Update. LateUpdate идёт после всех Update в кадре и держит alpha.
		// Живёт в PlayerModel (а не в EnemyModel): призраком-в-движении становится только игрок
		// (corpse-run при hp=0); enemy/animal просто умирают. Поля hp/action унаследованы.
		void LateUpdate()
		{
			bool ghost = hp == 0 && action != "dead";
			if (ghost)
				SetSpritesAlpha(0.5f);
			// При выходе из призрака (воскрешение / переход в action=dead) явно возвращаем непрозрачность.
			// Полагаться на то, что Spriter сам перезапишет color обратно в 1, нельзя: fallback root-SR,
			// не-Spriter сущности и скрытые в текущем кадре body-parts Spriter не трогает — без этого
			// возврата они застревают на alpha=0.5 (игрок «остаётся прозрачным» после получения HP).
			else if (_ghost)
				SetSpritesAlpha(1f);
			_ghost = ghost;
		}
	}
}
