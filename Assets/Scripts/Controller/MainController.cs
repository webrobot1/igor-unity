using UnityEngine;

public class MainController : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
        // заплатка для потери фокуса в webgl (если фокус падает на канву с игрой то обратно на странице html не доступны для ввода поля)
         public void Focus(int focus)
         {
            Debug.Log("Фокус: "+focus);

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
