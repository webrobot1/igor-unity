var WebGLDebug = 
{
    Check: function (map_id) 
	{
		container = window.parent.document.querySelector("#unity-api-container");
		if(container)
		{
			container.removeAttribute("disabled");
			window.parent.document.querySelector("#map_id").value = map_id;
		}
    }
}

mergeInto(LibraryManager.library, WebGLDebug);