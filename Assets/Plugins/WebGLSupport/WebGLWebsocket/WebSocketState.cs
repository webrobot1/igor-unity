namespace WebGLWebsocket
{

    /// <summary>
    /// в JS немного отличаются нумерации статусов соединений и нет New  https://developer.mozilla.org/en-US/docs/Web/API/WebSocket/readyState
    /// </summary>
    public enum WebSocketState : ushort
    {
        New = 0,
        Connecting = 0,
        Open = 1,
        Closing = 2,
        Closed = 3
    }
}