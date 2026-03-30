## WebSocket-плагины

Проект использует два WebSocket-плагина для разных платформ:

### WebsocketSharp (десктоп/мобильные)
Библиотека [websocket-sharp](https://github.com/sta/websocket-sharp) — `websocket-sharp.dll`.
Используется на ПК, Android, iOS. Подключается через `using WebSocketSharp;`.

**При миграции на Unity 6:** оригинальная DLL (собрана под .NET 3.5) несовместима с Unity 6. Заменена на версию с NuGet (`WebSocketSharp 1.0.3-rc11`). В новой версии убран `WebSocketState.New` — использовать `WebSocketState.Connecting` или `WebSocketState.Closed`.

### WebGLSupport/WebGLWebsocket (браузер)
Папка `Assets/Plugins/WebGLSupport/WebGLWebsocket/` — реализация WebSocket для WebGL-сборок через JavaScript (`WebSocket.jslib`). Предоставляет аналогичный API (методы `Close`, `Send`, `CloseAsync`) с совместимыми сигнатурами, но внутри вызывает браузерный WebSocket через `DllImport("__Internal")`. Использует `WebSocketSharp.CloseStatusCode` для совместимости типов параметров.

### Общее
Можно использовать любую библиотеку поддерживающую WebSocket, главное отключить алгоритм Нейгла который запрещает отправлять маленькие пакеты. При замене WebSocket-библиотеки WebGL-плагин может перестать работать (зависит от `WebSocketSharp.CloseStatusCode`).
