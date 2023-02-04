using System;
using System.Collections;
using UnityEngine;

namespace MyFantasy
{
	public class EnemyModel : ObjectModel
	{
		public override void SetData(ObjectRecive recive)
		{
			this.SetData((EnemyRecive)recive);
		}		
		
		protected void SetData(EnemyRecive recive)
		{
			base.SetData(recive);
		}
	}
}
