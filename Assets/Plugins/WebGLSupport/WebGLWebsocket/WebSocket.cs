﻿using System;
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
        /* WebSocket JSLIB functions */
        [DllImport("__Internal")]
        public static extern int WebSocketConnect();

        [DllImport("__Internal")]
        public static extern int WebSocketClose(int code, string reason);

        [DllImport("__Internal")]
        public static extern int WebSocketSend(byte[] dataPtr, int dataLength);

        [DllImport("__Internal")]
        public static extern int WebSocketGetState();

        /* объявим структуру функций которые будут вызываться из JS */
        public delegate void OnOpenCallback();
        public delegate void OnMessageCallback(System.IntPtr msgPtr, int msgSize);
        public delegate void OnErrorCallback(System.IntPtr errorPtr);
        public delegate void OnCloseCallback(int closeCode);

        // и для совместимости (тк нам надо их вызывать из вне а вызываются тока статические)
        public static event EventHandler _OnOpen;
        public static event EventHandler<MessageEventArgs> _OnMessage;
        public static event EventHandler<ErrorEventArgs> _OnError;
        public static event EventHandler<CloseEventArgs> _OnClose;

        
        // объявим список этих событий - обработчиков
        public event EventHandler OnOpen { add=> _OnOpen += value; remove => _OnOpen -= value; }
        public event EventHandler<MessageEventArgs> OnMessage { add => _OnMessage += value; remove => _OnMessage -= value; }
        public event EventHandler<ErrorEventArgs> OnError { add => _OnError += value; remove => _OnError -= value; }
        public event EventHandler<CloseEventArgs> OnClose { add => _OnClose += value; remove => _OnClose -= value; }


        /* WebSocket JSLIB callback setters and other functions */
        [DllImport("__Internal")]
        public static extern void WebSocketSetOnOpen(OnOpenCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnMessage(OnMessageCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnError(OnErrorCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketSetOnClose(OnCloseCallback callback);

        [DllImport("__Internal")]
        public static extern void WebSocketAllocate(string url);

        [DllImport("__Internal")]
        public static extern void WebSocketFree();
        public WebSocketSharp.WebSocketState ReadyState 
        { 
            get {
                return (WebSocketSharp.WebSocketState)WebSocketGetState();
            }
        }

        /// <summary>
        /// Constructor - receive JSLIB instance id of allocated socket
        /// </summary>
        public WebSocket(string url)
        {
            WebSocketAllocate(url);
        }

        // todo если понадобиться первый аргумент то надо вернуть вариент с instance_id
        [MonoPInvokeCallback(typeof(OnOpenCallback))]
        /// <summary>
        /// Delegates onOpen event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnOpenEvent()
        {
             _OnOpen?.Invoke(null, new EventArgs());
        }

        [MonoPInvokeCallback(typeof(OnMessageCallback))]
        /// <summary>
        /// Delegates onMessage event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnMessageEvent(System.IntPtr msgPtr, int msgSize)
        {

            byte[] msg = new byte[msgSize];
            Marshal.Copy(msgPtr, msg, 0, msgSize);

            var ev = new MessageEventArgs(msg);
            _OnMessage?.Invoke(null, ev);
        }

        [MonoPInvokeCallback(typeof(OnErrorCallback))]
        /// <summary>
        /// Delegates onError event from JSLIB to native sharp event
        /// </summary>
        /// <param name="errorMsg">Error message.</param>
        public static void DelegateOnErrorEvent(System.IntPtr errorPtr)
        {
            var ev = new ErrorEventArgs(Marshal.PtrToStringAuto(errorPtr));
            _OnError?.Invoke(null, ev);
        }

        [MonoPInvokeCallback(typeof(OnCloseCallback))]
        /// <summary>
        /// Delegate onClose event from JSLIB to native sharp event
        /// </summary>
        public static void DelegateOnCloseEvent(int closeCode)
        {
            var ev = new CloseEventArgs((WebSocketSharp.CloseStatusCode)closeCode);
            _OnClose?.Invoke(null, ev);
        }

        /// <summary>
        /// Destructor - notifies WebSocketFactory about it to remove JSLIB references
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="T:HybridWebSocket.WebSocket"/> is reclaimed by garbage collection.
        /// </summary>
        ~WebSocket()
        {
            WebSocketFree();
        }


        /// <summary>
        /// Open WebSocket connection
        /// </summary>
        public void Connect()
        {
            try
            {
                WebSocketSetOnOpen(DelegateOnOpenEvent);
                WebSocketSetOnMessage(DelegateOnMessageEvent);
                WebSocketSetOnError(DelegateOnErrorEvent);
                WebSocketSetOnClose(DelegateOnCloseEvent);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
            
            int ret = WebSocketConnect();

            if (ret < 0)
                GetErrorMessageFromCode(ret);
        }

        /// <summary>
        /// Close WebSocket connection with optional status code and reason.
        /// </summary>
        /// <param name="code">Close status code.</param>
        /// <param name="reason">Reason string.</param>
        public void Close(WebSocketSharp.CloseStatusCode code = WebSocketSharp.CloseStatusCode.Normal, string reason = null)
        {
            int ret = WebSocketClose((int)code, reason);

            if (ret < 0)
                GetErrorMessageFromCode(ret);
        }

        /// <summary>
        /// Send binary data over the socket.
        /// </summary>
        /// <param name="data">Payload data.</param>
        public void Send(byte[] data)
        {
            int ret = WebSocketSend(data, data.Length);

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