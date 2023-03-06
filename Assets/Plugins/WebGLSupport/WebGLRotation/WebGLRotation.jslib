var WebGLRotation = 
{
    WebGLRotationInit: function (mode) 
	{
		console.log(mode);
		if(mode)
		{
			if (!document.webkitIsFullScreen && !document.mozFullScreen && !document.msFullscreenElement)
			{
				screen.orientation.unlock();
			}
			else
				screen.orientation.lock("landscape");		
		}
		else
			screen.orientation.unlock();
    }
}
mergeInto(LibraryManager.library, WebGLRotation);