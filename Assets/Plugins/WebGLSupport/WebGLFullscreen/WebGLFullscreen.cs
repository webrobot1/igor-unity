using System;
using System.Runtime.InteropServices;
using AOT;

namespace WebGLSupport
{
    public class WebGLFullscreen
    {
        [DllImport("__Internal", EntryPoint = "requestFullscreen_FullscreenWebGL")]
        private static extern void requestFullscreen_FullscreenWebGL(Action<status> callback, string option);
        [DllImport("__Internal", EntryPoint = "exitFullscreen_FullscreenWebGL")]
        private static extern void exitFullscreen_FullscreenWebGL(Action<status> callback);
        [DllImport("__Internal", EntryPoint = "isFullscreen_FullscreenWebGL")]
        private static extern bool isFullscreen_FullscreenWebGL();
        [DllImport("__Internal", EntryPoint = "isFullscreenSupported_FullscreenWebGL")]
        private static extern bool isFullscreenSupported_FullscreenWebGL();
        [DllImport("__Internal", EntryPoint = "subscribeToFullscreenchangedEvent_FullscreenWebGL")]
        private static extern void subscribeToFullscreenchangedEvent_FullscreenWebGL(Action callback);
        [DllImport("__Internal", EntryPoint = "unsubscribeToFullscreenchangedEvent_FullscreenWebGL")]
        private static extern void unsubscribeToFullscreenchangedEvent_FullscreenWebGL();

        public enum status {
            Success = 0,
            Error = 1
        };

        public enum navigationUI {
            hide = 0,
            show = 1,
            auto = 2
        };

        public static event Action onfullscreenchange;
        private static event Action<status> statusCallbackEvent;

        [MonoPInvokeCallback(typeof(Action<status>))]
        private static void statusCallback(status status) {
            statusCallbackEvent?.Invoke(status);
            statusCallbackEvent = null;
        }

        [MonoPInvokeCallback(typeof(Action))]
        private static void fullscreenchangeCallback() {
            onfullscreenchange?.Invoke();
        }

        public static void requestFullscreen(Action<status> callback = null, navigationUI navUI = navigationUI.hide) {
            #if UNITY_WEBGL && !UNITY_EDITOR
                if (callback != null) statusCallbackEvent += callback;
                requestFullscreen_FullscreenWebGL(statusCallback, navUI.ToString());  
            #endif
        }

        public static void exitFullscreen(Action<status> callback = null) {
            #if UNITY_WEBGL && !UNITY_EDITOR
                if (callback != null) statusCallbackEvent += callback;
                exitFullscreen_FullscreenWebGL(statusCallback);
            #endif
        }

        public static bool isFullscreen() {
            #if UNITY_WEBGL && !UNITY_EDITOR
                return isFullscreen_FullscreenWebGL();
            #else
                return false;
            #endif
        }

        public static bool isFullscreenSupported() {
            #if UNITY_WEBGL && !UNITY_EDITOR
                return isFullscreenSupported_FullscreenWebGL();
            #else
                return false;
            #endif
        }

        public static void subscribeToFullscreenchangedEvent() {
            #if UNITY_WEBGL && !UNITY_EDITOR
                subscribeToFullscreenchangedEvent_FullscreenWebGL(fullscreenchangeCallback);
            #endif
        }

        public static void unsubscribeToFullscreenchangedEvent() {
            #if UNITY_WEBGL && !UNITY_EDITOR
                unsubscribeToFullscreenchangedEvent_FullscreenWebGL();
            #endif
        }
    }
}
