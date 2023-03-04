var WebGLFocus = 
{
	$init: false,
    Init: function (callback) 
	{
		if(init == false)
		{
			init = true;
		
			// +не работае нормально потеря фокуса в unity webgl поэтому ставим заплатку
			window.parent.document.addEventListener('click', function(e) 
			{
				Module['dynCall_vi'](callback, (e.target.id == "unity-canvas"?1:0));
			});
		}
    }
}
autoAddDeps(WebGLFocus, '$init');
mergeInto(LibraryManager.library, WebGLFocus);