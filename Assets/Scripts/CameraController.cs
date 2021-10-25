using UnityEngine;

[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    private void Update()
    {
        float screenRation = (float)Screen.width / (float)Screen.height;

        /// <summary>
        /// зона видимости вокруг игрока
        /// </summary>
        float targetRation = 16 / 16;
         
        if (screenRation != float.NaN) 
        {
            if (screenRation >= targetRation)
            { 
                GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = 16 / 2;
            }
            else
            {
                float defferenceSize = targetRation / screenRation;
                GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = 16 / 2 * defferenceSize;
            }
        }
    }
}
