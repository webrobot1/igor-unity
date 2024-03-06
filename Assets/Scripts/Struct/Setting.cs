using UnityEngine;
namespace MyFantasy
{
	public class Setting
	{
		public string type;
		public string title;


		public int? min;
		public int? max;

		public string value;	// текущее значение
		public string[] values;	// для выпаюающего меню
	}
}
