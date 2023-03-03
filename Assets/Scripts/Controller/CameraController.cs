using UnityEngine;
using MyFantasy;
using UnityEngine.UI;
using System;

// запуститься только в режиме Unity редактора в PlayMode
[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    private float last_size;

    private void Start()
    {
        last_size = GetComponent<Camera>().orthographicSize;
    }

    private void Update()
    {
        if (PlayerController.Instance.player != null)
		{
            transform.position = new Vector3(PlayerController.Instance.player.transform.position.x, PlayerController.Instance.player.transform.position.y, transform.position.z);
            float screenRation = (float)Screen.width / (float)Screen.height;

            /// <summary>
            /// зона видимости вокруг игрока
            /// </summary>
            float targetRation = 1;
            float size;
            if (screenRation != float.NaN) 
            {
                if (screenRation >= targetRation)
                { 
                    size = (PlayerController.Instance.player.lifeRadius - 0.5f) / 2;
                }
                else
                {
                    float defferenceSize = targetRation / screenRation;
                    size = (PlayerController.Instance.player.lifeRadius - 0.5f) / 2 * defferenceSize;
                }

                if(this.last_size != size)
                {
                    this.last_size = GetComponent<Camera>().orthographicSize = size;
                }
            }
        }
    }
}
