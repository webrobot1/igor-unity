using UnityEngine;

namespace MyFantasy
{
	public class NewObjectModel : ObjectModel
	{
		public void SetData(dynamic data)
		{
			base.SetData((NewObjectRecive)data);
		}
	}
}
