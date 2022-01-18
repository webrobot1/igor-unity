using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT;

namespace WebGLWebsocket
{

    /// <summary>
    /// WebSocket class bound to JSLIB.
    /// </summary>
    public class WebSocket
    {
        private static Dictionary<Int32, WebSocket> instances = new Dictionary<Int32, WebSocket>();
        protected int instanceId;


        /* WebSocket JSLIB functions */
        [DllImport("__Internal")]
        public static extern int WebSocketConnect(int instanceId);

        [DllImport("__Internal")]
        public static extern int WebSocketClose(int instanceId, int code, string reason);

        [DllImport("__Internal")]
        public static extern int WebSocketSend(int instanceId, byte[] dataPtr, int dataLength);

        [DllImport("__Internal")]
        public static extern int WebSocketGetState(int instanceId);

        /* объявим структуру функций которые будут вызываться из JS */
        public delegate void OnOpenCallback(int instanceId);
        public delegate void OnMessageCallback(int instanceId, System.IntPtr msgPtr, int msgSize);
        public delegate void OnErrorCallback(int instanceId, System.IntPtr errorPtr);
        public delegate void OnCloseCallback(int instanceId, int closeCode);

        // и для совместимости (тк нам надо их вызывать из вне а вызываются тока статические)
        public event EventHandler OnOpen;
        public event EventHandler<MessageEventArgs> OnMessage;
        public event EventHandler<ErrorEventArgs> OnError;
        public event EventHandler<CloseEventArgs> OnClose;

        /* WebSocket JSLIB callback setters and other functions */
        [DllImport("__Internal")]
        public static extern void WebSocketSetOnOpen(int instanceId, OnOpenCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnMessage(int instanceId, OnMessageCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnError(int instanceId, OnErrorCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnClose(int instanceId, OnCloseCallback callback);

        [DllImport("__Internal")]
        public static extern int WebSocketAllocate(string url);

        [DllImport("__Internal")]
        public static extern void WebSocketFree(int instanceId);
        public WebSocketSharp.WebSocketState ReadyState 
        { 
            get {
                return (WebSocketSharp.WebSocketState)WebSocketGetState(instanceId);
            }
        }

        /// <summary>
        /// Constructor - receive JSLIB instance id of allocated socket
        /// </summary>
        public WebSocket(string url)
        {
            this.instanceId = WebSocketAllocate(url);
        }

        // todo если понадобиться первый аргумент то надо вернуть вариент с instance_id
        [MonoPInvokeCallback(typeof(OnOpenCallback))]
        /// <summary>
        /// Delegates onOpen event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnOpenEvent(int instanceId)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                instanceRef.OnOpen?.Invoke(instanceRef, new EventArgs());
            }   
        }

        [MonoPInvokeCallback(typeof(OnMessageCallback))]
        /// <summary>
        /// Delegates onMessage event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnMessageEvent(int instanceId, System.IntPtr msgPtr, int msgSize)
        {

            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {

                byte[] msg = new byte[msgSize];
                Marshal.Copy(msgPtr, msg, 0, msgSize);

                var ev = new MessageEventArgs(msg);
                instanceRef.OnMessage?.Invoke(instanceRef, ev);
            }
        }

        [MonoPInvokeCallback(typeof(OnErrorCallback))]
        /// <summary>
        /// Delegates onError event from JSLIB to native sharp event
        /// </summary>
        /// <param name="errorMsg">Error message.</param>
        public static void DelegateOnErrorEvent(int instanceId, System.IntPtr errorPtr)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                var ev = new ErrorEventArgs(Marshal.PtrToStringAuto(errorPtr));
                instanceRef.OnError?.Invoke(instanceRef, ev);
            }
        }

        [MonoPInvokeCallback(typeof(OnCloseCallback))]
        /// <summary>
        /// Delegate onClose event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnCloseEvent(int instanceId, int closeCode)
        {
            WebSocket instanceRef;

            if (instances.TryGetValue(instanceId, out instanceRef))
            {
                var ev = new CloseEventArgs((WebSocketSharp.CloseStatusCode)closeCode);

                instanceRef.OnClose?.Invoke(instanceRef, ev);
            }
        }

        /// <summary>
        /// Destructor - notifies WebSocketFactory about it to remove JSLIB references
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="T:HybridWebSocket.WebSocket"/> is reclaimed by garbage collection.
        /// </summary>
        ~WebSocket()
        {
            instances.Remove(instanceId);
            WebSocketFree(instanceId);
        }

        /// <summary>
        /// Open WebSocket connection
        /// </summary>
        public void Connect()
        {

            try
            {
                // todo в js события так же хранить в отдельных элементах массива где ид  = instanceId
                WebSocketSetOnOpen(instanceId, DelegateOnOpenEvent);
                WebSocketSetOnMessage(instanceId, DelegateOnMessageEvent);
                WebSocketSetOnError(instanceId, DelegateOnErrorEvent);
                WebSocketSetOnClose(instanceId, DelegateOnCloseEvent);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }

            int ret = WebSocketConnect(instanceId);

            if (ret < 0)
                GetErrorMessageFromCode(ret);
            else
                instances.Add(instanceId, this); // добавим наш экземляр класса с привязанными событиями в статическую переменную тк из js можно только к статик методам событиям обращаться
        }

        /// <summary>
        /// Close WebSocket connection with optional status code and reason.
        /// </summary>
        /// <param name="code">Close status code.</param>
        /// <param name="reason">Reason string.</param>
        public void Close(WebSocketSharp.CloseStatusCode code = WebSocketSharp.CloseStatusCode.Normal, string reason = null)
        {
            int ret = WebSocketClose(instanceId, (int)code, reason);

            if (ret < 0)
                GetErrorMessageFromCode(ret);
        }

        /// <summary>
        /// Send binary data over the socket.
        /// </summary>
        /// <param name="data">Payload data.</param>
        public void Send(byte[] data)
        {
            int ret = WebSocketSend(instanceId, data, data.Length);

            if (ret < 0)
                GetErrorMessageFromCode(ret);
        }

        /// <summary>
        /// Преобразует JS ошибки в исключения
        /// </summary>
        /// <returns>Instance of an exception.</returns>
        /// <param name="errorCode">Error code.</param>
        /// <param name="inner">Inner exception</param>
        public static void GetErrorMessageFromCode(int errorCode)
        {
            switch (errorCode)
            {
                case -1: throw new Exception("WebSocket instance not found.");
                case -2: throw new Exception("WebSocket is already connected or in connecting state.");
                case -3: throw new Exception("WebSocket is not connected.");
                case -4: throw new Exception("WebSocket is already closing.");
                case -5: throw new Exception("WebSocket is already closed.");
                case -6: throw new Exception("WebSocket is not in open state.");
                case -7: throw new Exception("Cannot close WebSocket. An invalid code was specified or reason is too long.");
                default: throw new Exception("Unknown error.");
            }
        }
    }

    public class ErrorEventArgs : EventArgs
    {
        internal ErrorEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    public class CloseEventArgs : EventArgs
    {
        internal CloseEventArgs(WebSocketSharp.CloseStatusCode code)
        {
            Code = code;
        }

        public WebSocketSharp.CloseStatusCode Code { get; }
    }

    public class MessageEventArgs : EventArgs
    {
        internal MessageEventArgs(byte[] message)
        {
            RawData = message;
        }

        public byte[] RawData { get; }
    }
}