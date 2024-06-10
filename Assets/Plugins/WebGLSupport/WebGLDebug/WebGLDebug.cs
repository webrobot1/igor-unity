using AOT;
using System.Runtime.InteropServices; // for DllImport

namespace WebGLSupport
{
    public static class WebGLDebug
    {
        public delegate void OnSendCallback(System.IntPtr errorPtr);

        [DllImport("__Internal")]
        public static extern void DebugSetOnSend(OnSendCallback callback);
        [DllImport("__Internal")]
        public static extern void Check(int map_id);


        [MonoPInvokeCallback(typeof(OnSendCallback))]
        public static void DelegateOnSend(System.IntPtr stringPtr)
        {
            Mmogick.ConnectController.Put2Send(Marshal.PtrToStringAuto(stringPtr));
        }

        public static void DebugCheck(int map_id)
        {
            DebugSetOnSend(DelegateOnSend);
            Check(map_id);
        }
    }
}
