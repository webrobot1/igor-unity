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
		instances: {},
		
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

		/* Last instance ID */
		lastId: 0,

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
	WebSocketSetOnOpen: function(instanceId, callback) {

		webSocketState.onOpen = callback;
	},

	/**
	 * Set onMessage callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnMessage: function(instanceId, callback) {

		webSocketState.onMessage = callback;

	},

	/**
	 * Set onError callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnError: function(instanceId, callback) {

		webSocketState.onError = callback;

	},

	/**
	 * Set onClose callback
	 * 
	 * @param callback Reference to C# static function
	 */
	WebSocketSetOnClose: function(instanceId, callback) {

		webSocketState.onClose = callback;

	},

	/**
	 * Allocate new WebSocket instance struct
	 * 
	 * @param url Server URL
	 */
	WebSocketAllocate: function(url) {

		var urlStr = Pointer_stringify(url);
		var id = webSocketState.lastId++;

		webSocketState.instances[id] = {
			url: urlStr,
			ws: null
		};

		return id;

	},

	/**
	 * Remove reference to WebSocket instance
	 * 
	 * If socket is not closed function will close it but onClose event will not be emitted because
	 * this function should be invoked by C# WebSocket destructor.
	 * 
	 * @param instanceId Instance ID
	 */
	WebSocketFree: function(instanceId) {

		var instance = webSocketState.instances[instanceId];

		if (!instance) return 0;

		// Close if not closed
		if (instance.ws !== null && instance.ws.readyState < 2)
			instance.ws.close();

		// Remove reference
		delete webSocketState.instances[instanceId];

		return 0;

	},

	/**
	 * Connect WebSocket to the server
	 * 
	 * @param instanceId Instance ID
	 */
	WebSocketConnect: function(instanceId) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws !== null)
			return -2;

		instance.ws = new WebSocket(instance.url);

		instance.ws.binaryType = 'arraybuffer';

		instance.ws.onopen = function() 
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
					instance.ws.send(message);
				});
				
				/* Очищаем очередь */
				webSocketState.queue = [];
			}
				
			if (webSocketState.onOpen)
				Module['dynCall_vi'](webSocketState.onOpen, instanceId);
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
					Module['dynCall_viii'](webSocketState.onMessage, instanceId, buffer, dataBuffer.length);
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
					Module['dynCall_vii'](webSocketState.onError, instanceId, msgBuffer);
				} finally {
					_free(msgBuffer);
				}

			}

		};

		instance.ws.onclose = function(ev) {

			if (webSocketState.debug)
			{
				webSocketState.debug.setAttribute("disabled", "disabled");
				document.querySelector("#map_id").value = '';
				document.querySelector("#perfomance").innerHTML = 'войдите в игру';

				webSocketState.Log("Closed");
			}

			if (webSocketState.onClose)
				Module['dynCall_vii'](webSocketState.onClose, instanceId, ev.code);

			delete instance.ws;

		};

		return 0;

	},

	/**
	 * Close WebSocket connection
	 * 
	 * @param instanceId Instance ID
	 * @param code Close status code
	 * @param reasonPtr Pointer to reason string
	 */
	WebSocketClose: function(instanceId, code, reasonPtr) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws === null)
			return -3;

		if (instance.ws.readyState === 2)
			return -4;

		if (instance.ws.readyState === 3)
			return -5;

		var reason = ( reasonPtr ? Pointer_stringify(reasonPtr) : undefined );
		
		try {
			instance.ws.close(code, reason);
		} catch(err) {
			return -7;
		}

		return 0;

	},

	/**
	 * Send message over WebSocket
	 * 
	 * @param instanceId Instance ID
	 * @param bufferPtr Pointer to the message buffer
	 * @param length Length of the message in the buffer
	 */
	WebSocketSend: function(instanceId, bufferPtr, length) {
	
		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;
		
		if (instance.ws === null)
			return -3;

		if (instance.ws.readyState !== 1)
		{
			/* Если конект не открыт, добавляем сообщение в очередь */
			webSocketState.queue.push(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));
			return 0;
			
			/* return -6; */
		}

		instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));

		return 0;

	},

	/**
	 * Return WebSocket readyState
	 * 
	 * @param instanceId Instance ID
	 */
	WebSocketGetState: function(instanceId) {

		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;

		if (instance.ws)
			return instance.ws.readyState;
		else
			return 3;

	}
};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);