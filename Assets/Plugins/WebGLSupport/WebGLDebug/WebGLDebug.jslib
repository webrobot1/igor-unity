var WebGLDebug = 
{
    Check: function (token) 
	{
		container = document.querySelector("#unity-api-container");
		if(container)
		{
			.value = ;
			var tokens = container.querySelectorAll(".token");
			for (var i = 0; i < tokens.length; i++) {
			  tokens[i].value = Pointer_stringify(token);
			} 
			container.removeAttribute("disabled");
		}
    }
}

mergeInto(LibraryManager.library, WebGLDebug);