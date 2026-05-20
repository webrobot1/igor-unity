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
	}
}
