var WebGLDebug = 
{
	$DebugState: 
	{
		init: false,
		OnSend: null
	},
	DebugSetOnSend: function(callback) 
	{
		DebugState.OnSend = callback;
	},
    Check: function (map_id) 
	{
		container = window.document.querySelector("#unity-api-container");
		if(!container) container = window.parent.document.querySelector("#unity-api-container");
		
		if(container)
		{
			container.querySelector("#unity-api-command").removeAttribute("disabled");
			container.querySelector("#map_id").value = map_id;
			
			// для отладки в админке 
			if(DebugState.init == false)
			{
				DebugState.init = true;
				container.querySelector('#unity-api-command').addEventListener('submit', function(e) 
				{
					e.preventDefault();
					const data = new FormData(e.target);
					let value = Object.fromEntries(data.entries());
					
					let msg = JSON.stringify(value);
					let msgBytes = lengthBytesUTF8(msg) + 1;
					let msgBuffer = _malloc(msgBytes);
					stringToUTF8(msg, msgBuffer, msgBytes);
					
					try 
					{
						Module['dynCall_vi'](DebugState.OnSend, msgBuffer);
					} 
					finally 
					{
						_free(msgBuffer);
					}	
				});
			}
		}
    }
}
autoAddDeps(WebGLDebug, '$DebugState');
mergeInto(LibraryManager.library, WebGLDebug);