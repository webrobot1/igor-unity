using UnityEngine;

namespace MyFantasy
{
	public class NewObjectModel : ObjectModel
	{
		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}		
		
		protected void SetData(NewObjectRecive recive)
		{
			base.SetData(recive);
		}
	}
}
