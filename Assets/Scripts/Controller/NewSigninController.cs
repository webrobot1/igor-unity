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
                if (WebGLFullscreen.isFullscreenSupported())                    //if fullscreen is supported
                {
                    WebGLFullscreen.onfullscreenchange += () => {               //and then I add a callback that will run once the user enters or exits fullscreen
                        if (WebGLFullscreen.isFullscreen())                     //if it's fullscreen
                        {
                           // WebGLRotation.Rotation(1);
                        }
                        else                                                    //otherwise do the opposite
                        {
                           // WebGLRotation.Rotation(0);
                        }
                    };
                    WebGLFullscreen.subscribeToFullscreenchangedEvent();        //I'm interested in listening to fullscreen changes, so I subscribe to the event.
                }
            #else
                Screen.orientation = ScreenOrientation.Portrait;
                Screen.autorotateToPortrait = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            #endif

            base.Start();
        }

        public static void EnterFullScreenMode()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
                WebGLFullscreen.requestFullscreen(stat => {
                    if (stat == WebGLFullscreen.status.Success) 
                    {
                        WebGLRotation.Rotation(1);
                    }
                });  
#endif
        }

        public void ExitFullscreenMode()
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
                WebGLFullscreen.exitFullscreen(stat => {
                    if (stat == WebGLFullscreen.status.Success)
                    {

                    }
                });
            #endif
        }
    }
}
