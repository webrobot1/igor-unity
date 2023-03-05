var WebGLFocus = 
{
	$init: false,
    Init: function () 
	{
		if(init == false)
		{
			init = true;
			window.document.addEventListener("touchstart",function(e)
			{
				if(e.target.id=="unity-canvas")
				{
					window.focus();
				}
			});			
		}
    }
}
autoAddDeps(WebGLFocus, '$init');
mergeInto(LibraryManager.library, WebGLFocus);