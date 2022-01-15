using UnityEngine;

// запуститься только в режиме Unity редактора в PlayMode
[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    float last_size;

    private void Start()
    {
        last_size = GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize;
    }


    private void Update()
    {
        float screenRation = (float)Screen.width / (float)Screen.height;

        /// <summary>
        /// зона видимости вокруг игрока
        /// </summary>
        float targetRation = 12 / 12;
        float size;
        if (screenRation != float.NaN) 
        {
            if (screenRation >= targetRation)
            { 
                size = 12 / 2;
            }
            else
            {
                float defferenceSize = targetRation / screenRation;
                size = 12 / 2 * defferenceSize;
            }

            if(this.last_size != size)
            {
                this.last_size = GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = size;
            }
        }
    }
}
