var WebGLInputMobile = {
    $instances: [],

    WebGLInputMobileRegister: function (touchend) {
        var id = instances.push(null) - 1;

        document.body.addEventListener("touchend", function () {
            document.body.removeEventListener("touchend", arguments.callee);
			Module['dynCall_vi'](touchend, [id]);
        });

        return id;
    },
    WebGLInputMobileOnFocusOut: function (id, focusout) {
        document.body.addEventListener("focusout", function () {
            document.body.removeEventListener("focusout", arguments.callee);
            Module['dynCall_vi'](focusout, [id]);
        });
    },
}

autoAddDeps(WebGLInputMobile, '$instances');
mergeInto(LibraryManager.library, WebGLInputMobile);