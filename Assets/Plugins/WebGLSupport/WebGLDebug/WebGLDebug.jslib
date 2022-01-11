var WebGLDebug = 
{
    Check: function (token) 
	{
		container = document.querySelector("#unity-api-container");
		if(container)
		{
			container.querySelectorAll(".token").value = Pointer_stringify(token);
			container.removeAttribute("disabled");
		}
    }
}

mergeInto(LibraryManager.library, WebGLDebug);