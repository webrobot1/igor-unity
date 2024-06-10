using System;
using System.Collections.Generic;
using UnityEngine;
namespace Mmogick
{
	public class SettingRecive
	{
		public string type;
		public string title;


		public int? min;
		public int? max;

		public string value;								// текущее значение
		public Dictionary<string, string> values = null;    // для выпаюающего меню
    }
}
