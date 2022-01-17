/*
 * unity-websocket-webgl
 * 
 * @author Jiri Hybek <jiri@hybek.cz>
 * @copyright 2018 Jiri Hybek <jiri@hybek.cz>
 * @license Apache 2.0 - See LICENSE file distributed with this source code.
 */

var LibraryWebSocket = {
	$webSocketState: {
		/*
		 * Map of instances
		 * 
		 * Instance structure:
		 * {
		 * 	url: string,
		 * 	ws: WebSocket
		 * }
		 */
		instance: {},
		
		// объект с контейнером отладки
		debug: null,
		
		// функция логирования для отладки
		Log: function(json){
			if (webSocketState.debug)
			{
				if(webSocketState.debug.querySelector('#debug').value.length > 500)
					webSocketState.debug.querySelector('#debug').value = json;
				else
					webSocketState.debug.querySelector('#debug').value = json + "\n" + webSocketState.debug.querySelector('#debug').value;
			}
		}, 

		/* очередь сообщений */
		queue: [],

		/* Event listeners */
		onOpen: null,
		onMesssage: null,
		onError: null,
		onClose: null,
	},
	
	/**
	 * Set onOpen callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnOpen: function(callback) {

		webSocketState.onOpen = callback;
	},

	/**
	 * Set onMessage callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnMessage: function(callback) {

		webSocketState.onMessage = callback;

	},

	/**
	 * Set onError callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnError: function(callback) {

		webSocketState.onError = callback;

	},

	/**
	 * Set onClose callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnClose: function(callback) {

		webSocketState.onClose = callback;

	},

	/**
	 * Allocate new WebSocket instance struct
	 * 
	 * @param url Server URL
	 */
	WebSocketAllocate: function(url) {

		var urlStr = Pointer_stringify(url);

		webSocketState.instance = 
		{
			url: urlStr,
			ws: null
		};
	},

	/**
	 * Remove reference to WebSocket instance
	 * 
	 * If socket is not closed function will close it but onClose event will not be emitted because
	 * this function should be invoked by C# WebSocket destructor.
	 * 
	 */
	WebSocketFree: function() {

		if (!webSocketState.instance) return 0;

		// Close if not closed
		if (webSocketState.instance.ws !== null && webSocketState.instance.ws.readyState < 2)
			webSocketState.instance.ws.close();

		// Remove reference
		delete webSocketState.instance;

		return 0;

	},

	/**
	 * Connect WebSocket to the server
	 * 
	 */
	WebSocketConnect: function() {

		if (!webSocketState.instance) return -1;

		if (webSocketState.instance.ws !== null)
			return -2;

		webSocketState.instance.ws = new WebSocket(webSocketState.instance.url);

		webSocketState.instance.ws.binaryType = 'arraybuffer';

		webSocketState.instance.ws.onopen = function() 
		{
			webSocketState.debug = document.querySelector("#unity-api-container");
			if (webSocketState.debug)
			{
				webSocketState.Log("Connected");
			}
			
			/* если от момента инициализации до конекта есть неотправленные сообщения */
			if (webSocketState.queue.length>0)
			{
				webSocketState.queue.forEach(function(message, i, arr) {
					webSocketState.instance.ws.send(message);
				});
				
				/* Очищаем очередь */
				webSocketState.queue = [];
			}
				
			if (webSocketState.onOpen)
				Runtime.dynCall('vi', webSocketState.onOpen);
		};

		instance.ws.onmessage = function(ev) {

			if (webSocketState.debug)
			{
				webSocketState.Log(ev.data);
			}

			if (webSocketState.onMessage === null)
				return;

			if (ev.data instanceof ArrayBuffer)
			{
				var dataBuffer = new Uint8Array(ev.data);
			}
	        else if (typeof ev.data === 'string') 
			{
				var arrayBuffer = new ArrayBuffer(ev.data.length);
				var dataBuffer = new Uint8Array(arrayBuffer);
				
				// read string message into data buffer
				dataBuffer.forEach(function(_, i) {dataBuffer[i] = ev.data.charCodeAt(i);});
	        }
					
			if (dataBuffer != null) 
			{
				var buffer = _malloc(dataBuffer.length);
				HEAPU8.set(dataBuffer, buffer);   

				try {
					Runtime.dynCall('viii', webSocketState.onMessage, [ buffer, dataBuffer.length ]);
				} finally {
					_free(buffer);
				}
	        }	
		};

		instance.ws.onerror = function(ev) {
			
			if (webSocketState.debug)
				webSocketState.Log("Error occured");

			if (webSocketState.onError) {
				
				var msg = "WebSocket error.";
				var msgBytes = lengthBytesUTF8(msg);
				var msgBuffer = _malloc(msgBytes + 1);
				stringToUTF8(msg, msgBuffer, msgBytes);

				try {
					Runtime.dynCall('vii', webSocketState.onError, [ msgBuffer ]);
				} finally {
					_free(msgBuffer);
				}

			}

		};

		instance.ws.onclose = function(ev) {

			if (webSocketState.debug)
			{
				webSocketState.debug.setAttribute("disabled", "disabled");
				var tokens = webSocketState.debug.querySelectorAll(".token");
				for (var i = 0; i < tokens.length; i++) {
				  tokens[i].value = "";
				} 
				
				webSocketState.Log("Closed");
			}

			if (webSocketState.onClose)
				Runtime.dynCall('vii', webSocketState.onClose, [ ev.code ]);

			delete instance.ws;

		};

		return 0;

	},

	/**
	 * Close WebSocket connection
	 * 
	 * @param code Close status code
	 * @param reasonPtr Pointer to reason string
	 */
	WebSocketClose: function(code, reasonPtr) {

		if (!webSocketState.instance) return -1;

		if (webSocketState.instance.ws === null)
			return -3;

		if (webSocketState.instance.ws.readyState === 2)
			return -4;

		if (webSocketState.instance.ws.readyState === 3)
			return -5;

		var reason = ( reasonPtr ? Pointer_stringify(reasonPtr) : undefined );
		
		try {
			webSocketState.instance.ws.close(code, reason);
		} catch(err) {
			return -7;
		}

		return 0;

	},

	/**
	 * Send message over WebSocket
	 * 
	 * @param bufferPtr Pointer to the message buffer
	 * @param length Length of the message in the buffer
	 */
	WebSocketSend: function(bufferPtr, length) {
	
		if (!webSocketState.instance) return -1;
		
		if (webSocketState.instance.ws === null)
			return -3;
console.log(webSocketState.instance.ws);
		if (webSocketState.instance.ws.readyState !== 1)
		{
			/* Если конект не открыт, добавляем сообщение в очередь */
			webSocketState.queue.push(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));
			return 0;
			
			/* return -6; */
		}

		webSocketState.instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));

		return 0;

	},

	/**
	 * Return WebSocket readyState
	 * 
	 */
	WebSocketGetState: function() {

		if (!webSocketState.instance) return -1;

		if (webSocketState.instance.ws)
			return webSocketState.instance.ws.readyState;
		else
			return 3;

	}
};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);