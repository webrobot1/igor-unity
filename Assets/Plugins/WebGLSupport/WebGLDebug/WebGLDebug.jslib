var WebGLDebug = 
{
    Check: function (map_id) 
	{
		container = document.querySelector("#unity-api-container");
		if(container)
		{
			container.removeAttribute("disabled");
			document.querySelector("#map_id").value = map_id;
		}
    }
}

mergeInto(LibraryManager.library, WebGLDebug);