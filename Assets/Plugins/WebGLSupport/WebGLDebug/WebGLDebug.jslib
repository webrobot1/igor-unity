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
		container = window.parent.document.querySelector("#unity-api-container");
		if(container)
		{
			container.removeAttribute("disabled");
			window.parent.document.querySelector("#map_id").value = map_id;
			
			// для отладки в админке 
			if(DebugState.init == false && window.parent.document.querySelector('#unity-api-container'))
			{
				DebugState.init = true;
				if(window.parent.document.querySelector('#map_id'))
				{
				  setInterval(() => 
				  {
					if(window.parent.document.querySelector('#map_id').value)
					{
						window.parent.document.querySelector('#perfomance').innerHTML = '<span class="glyphicon-refresh-animate glyphicon glyphicon-refresh"></span>';
						fetch('/server/api/log_perfomance/'+window.parent.document.querySelector('#map_id').value).then((response) => response.text()).then((data) => window.parent.document.querySelector('#perfomance').innerHTML = data);
					}
				  }, 10000);
				}
			  
				window.parent.document.querySelector('#unity-api-container').addEventListener('submit', function(e) 
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