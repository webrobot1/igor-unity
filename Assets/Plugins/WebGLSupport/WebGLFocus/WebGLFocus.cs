using System;
using AOT;
using System.Runtime.InteropServices; // for DllImport
using UnityEngine;

namespace WebGLSupport
{
    public static class WebGLFocus
    {
        [DllImport("__Internal")]
        public static extern void Init();

        public static void FocusInit()
        {
            Init();
        }
    }
}
