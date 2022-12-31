using System;
using AOT;
using System.Runtime.InteropServices; // for DllImport
using UnityEngine;

namespace WebGLSupport
{
    public static class WebGLDebug
    {
		[DllImport("__Internal")]
		public static extern void Check(int map_id);
    }
}
