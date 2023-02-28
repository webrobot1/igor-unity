using UnityEngine;
using MyFantasy;
using UnityEngine.UI;
using System;

// запуститься только в режиме Unity редактора в PlayMode
[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    public Image hpFrame;
    public Image mpFrame;

    private float last_size;

    private void Start()
    {
        last_size = GetComponent<Camera>().orthographicSize;

        if (hpFrame == null)
            ConnectController.Error("не присвоен GameObject для линии жизни");

        if (mpFrame == null)
            ConnectController.Error("не присвоен GameObject для линии маны");
    }

    private void Update()
    {
        if (ConnectController.player != null)
		{
            transform.position = new Vector3(ConnectController.player.transform.position.x, ConnectController.player.transform.position.y, transform.position.z);
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
                    this.last_size = GetComponent<Camera>().orthographicSize = size;
                }
            }
        }
    }
}
