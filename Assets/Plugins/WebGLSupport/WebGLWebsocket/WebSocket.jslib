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
		
		// осуществляется ли на странице куда встроена игра отладка (наличие функции debug_unity в java script сайта)
		debug: null, 

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

		var urlStr = UTF8ToString(url);
		var id = webSocketState.lastId++;

		webSocketState.instances[id] = {
			url: urlStr,
			ws: null,
			login: null,
			password: null
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
	
	WebsocketSetCredentials: function(instanceId, login, password, preAuth) 
	{
		webSocketState.instances[instanceId].login = UTF8ToString(login);
		webSocketState.instances[instanceId].password = UTF8ToString(password);
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

		let url = instance.url;
		
		if(instance.login!==null && instance.password!==null)
			url += '/?HTTP_AUTHORIZATION='+btoa(instance.login+':'+instance.password);

		instance.ws = new WebSocket(url);
		instance.ws.binaryType = 'arraybuffer';

		instance.ws.onopen = function() 
		{
			// если на странце куда встроена игра реализована функция debug
			webSocketState.debug = (typeof debug_unity === "function"?debug_unity:null);
			
			// иногда во фреймв страивают игру на сранцие
            if (!webSocketState.debug) webSocketState.debug = (typeof window.parent.debug_unity === "function"?window.parent.debug_unity:null); 
			
			if (webSocketState.debug)
				webSocketState.debug("Connected");
			
			
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
				webSocketState.debug(ev.data);

			if (webSocketState.onMessage === null)
				return;

			if (ev.data instanceof ArrayBuffer)
			{
				var dataBuffer = new Uint8Array(ev.data);
			}
	        else if (typeof ev.data === 'string') 
			{
				var dataBuffer = new TextEncoder("utf-8").encode(ev.data);
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
				webSocketState.debug("Error occured");

			if (webSocketState.onError) {
				
				var msg = "WebSocket error.";
				var msgBytes = lengthBytesUTF8(msg) + 1;
				var msgBuffer = _malloc(msgBytes);
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
				webSocketState.debug("Closed");

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

		var reason = ( reasonPtr ? UTF8ToString(reasonPtr) : undefined );
		
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
	WebSocketSend: function(instanceId, bufferPtr, length){
	
		var instance = webSocketState.instances[instanceId];
		if (!instance) return -1;
		
		if (instance.ws === null)
			return -3;

		let message = HEAPU8.buffer.slice(bufferPtr, bufferPtr + length);
		
		if (webSocketState.debug)
			webSocketState.debug(new TextDecoder("utf-8").decode(message), false);
		
		if (instance.ws.readyState !== 1)
		{
			/* Если конект не открыт, добавляем сообщение в очередь */
			webSocketState.queue.push(message);
			return 0;
			
			/* return -6; */
		}

		instance.ws.send(message);
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