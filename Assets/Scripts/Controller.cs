using UnityEngine;

public class Controller : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
        // �������� ��� ������ ������ � webgl 
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
