using UnityEngine;
using UnityEngine.UI;
using WebGLSupport;

namespace MyFantasy
{
    public class NewSigninController : SigninController
    {
        protected override void Awake()
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
                WebGLRotation.Rotation(0);
            #else
                Screen.orientation = ScreenOrientation.Portrait;
                Screen.autorotateToPortrait = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            #endif

            base.Start();
        }

        public static void EnterFullScreenMode()
        {
            // в версии отладки полный экран не делаем
            #if !DEVELOPMENT_BUILD
                #if UNITY_WEBGL && !UNITY_EDITOR 
                    WebGLFullscreen.requestFullscreen();  
                #else
                    Screen.fullScreen = true;
                #endif
            #endif
        }

        public void ExitFullscreenMode()
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
                WebGLFullscreen.exitFullscreen();
            #else
                Screen.fullScreen = false;
            #endif
        }
    }
}
