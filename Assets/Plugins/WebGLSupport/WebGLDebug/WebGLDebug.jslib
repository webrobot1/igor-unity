var WebGLDebug = 
{
    Check: function (token, map_id) 
	{
		container = document.querySelector("#unity-api-container");
		if(container)
		{
			var tokens = container.querySelectorAll(".token");
			for (var i = 0, token = Pointer_stringify(token); i < tokens.length; i++)
			{
			  tokens[i].value = token;
			} 
			
			container.removeAttribute("disabled");
			document.querySelector("#map_id").value = map_id;
		}
    }
}

mergeInto(LibraryManager.library, WebGLDebug);