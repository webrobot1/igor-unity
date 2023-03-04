using System;
using AOT;
using System.Runtime.InteropServices; // for DllImport
using UnityEngine;

namespace WebGLSupport
{
    public static class WebGLFocus
    {
        public delegate void OnFocusCallback(int focus);
        [DllImport("__Internal")]
        public static extern void Init(OnFocusCallback callback);

        [MonoPInvokeCallback(typeof(OnFocusCallback))]
        public static void DelegateOnFocus(int focus)
        {
			Debug.Log("Фокус JS: "+focus);

            #if UNITY_WEBGL && !UNITY_EDITOR
                if (focus == 0) 
                {
                    Input.ResetInputAxes();
                    UnityEngine.WebGLInput.captureAllKeyboardInput = false;
                } 
                else
                {
                    UnityEngine.WebGLInput.captureAllKeyboardInput = true;
                    Input.ResetInputAxes();
                }       
            #endif
        }

        public static void FocusInit()
        {
            Init(DelegateOnFocus);
        }
    }
}
