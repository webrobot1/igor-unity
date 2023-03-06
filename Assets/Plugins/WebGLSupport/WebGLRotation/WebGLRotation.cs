using System;
using AOT;
using System.Runtime.InteropServices; // for DllImport
using UnityEngine;

namespace WebGLSupport
{
    public static class WebGLRotation
    {
        [DllImport("__Internal")]
        public static extern void WebGLRotationInit(int mode);

        public static void Rotation(int mode)
        {
            WebGLRotationInit(mode);
        }      
    }
}
