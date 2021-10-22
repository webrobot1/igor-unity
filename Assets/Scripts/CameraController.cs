using UnityEngine;

[ExecuteInEditMode]
public class CameraController : MonoBehaviour
{
    private Camera m_camera;
    private float lastAspect;

    [SerializeField]
    private float m_orthographicSize = 8f;
    public GameObject vcam;

    private void OnEnable()
    {
        RefreshCamera();
    }

    private void Update()
    {
        float aspect = m_camera.aspect;
        if (aspect != lastAspect)
            AdjustCamera(aspect);
    }

    public void RefreshCamera()
    {
        if (m_camera == null)
            m_camera = GetComponent<Camera>();

        AdjustCamera(m_camera.aspect);
    }

    private void AdjustCamera(float aspect)
    {
        lastAspect = aspect;
        float _1OverAspect = 1f / aspect;
        vcam.GetComponent<Cinemachine.CinemachineVirtualCamera>().m_Lens.OrthographicSize = m_orthographicSize * _1OverAspect;    
    }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RefreshCamera();
        }
#endif
}
