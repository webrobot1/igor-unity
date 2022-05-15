using UnityEngine;

public class MainController : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
        // заплатка для потери фокуса в webgl 
         public void Focus(int focus)
         {
            if (focus == 0) 
            {
                Input.ResetInputAxes();
                UnityEngine.WebGLInput.captureAllKeyboardInput = false;
            } else
            {
                WebGLInput.captureAllKeyboardInput = true;
                Input.ResetInputAxes();
            }
        }        
#endif
}
