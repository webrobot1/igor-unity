using UnityEngine;

[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    private void Update()
    {
        float screenRation = (float)Screen.width / (float)Screen.height;
        float targetRation = 16 / 32;
         
        if (screenRation != float.NaN) 
        {
            Debug.Log(screenRation);
            Debug.Log(targetRation);
            if (screenRation >= targetRation)
            { 
                GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = 32 / 2;
            }
            else
            {
                float defferenceSize = targetRation / screenRation;
                Debug.Log(defferenceSize+"ddd");
                GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = 32 / 2 * defferenceSize;
            }
        }
    }
}
