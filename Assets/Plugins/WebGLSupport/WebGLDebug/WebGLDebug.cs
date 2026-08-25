using AOT;
using System;
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

        /// <summary>
        /// Кому уходит собранное браузером. Получателя называет вызывающий: пакет поддержки WebGL —
        /// привязка к браузеру, и адресат данных лежит выше него. Имя адресата, вписанное сюда, делало
        /// бы пакет зависимым от того, кто его же и вызывает.
        /// </summary>
        private static Action<string> send;

        [MonoPInvokeCallback(typeof(OnSendCallback))]
        public static void DelegateOnSend(System.IntPtr stringPtr)
        {
            if (send != null)
                send(Marshal.PtrToStringAuto(stringPtr));
        }

        public static void DebugCheck(int map_id, Action<string> send)
        {
            if (send == null)
                throw new ArgumentNullException("send", "проверке браузера не назван получатель собранного");

            WebGLDebug.send = send;

            DebugSetOnSend(DelegateOnSend);
            Check(map_id);
        }
    }
}
