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
                Debug.Log("������ ������");
            } else
            {
                WebGLInput.captureAllKeyboardInput = true;
                Input.ResetInputAxes();
                Debug.Log("�����");
            }
        }        
#endif
}
