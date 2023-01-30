using UnityEngine;

// запуститься только в режиме Unity редактора в PlayMode
[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    public Transform player;
    float last_size;

    private void Start()
    {
        last_size = GetComponent<Camera>().orthographicSize;
    }


    private void Update()
    {
        if (player != null)
            transform.position = new Vector3(player.position.x, player.position.y, transform.position.z);

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
