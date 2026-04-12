using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    public class Tooltip : MonoBehaviour
    {
        [SerializeField]
        private CanvasGroup canvasGroup;

        [SerializeField]
        private Text tooltipText;

        void Awake()
        {
            if (canvasGroup == null)
                ConnectController.Error("не указан CanvasGroup для Tooltip");

            if (tooltipText == null)
                ConnectController.Error("не указан Text для Tooltip");

            canvasGroup.alpha = 0;
            canvasGroup.blocksRaycasts = false;
        }

        public void Show(Vector3 position, string text)
        {
            tooltipText.text = text;
            transform.position = position;
            canvasGroup.alpha = 1;
        }

        public void Hide()
        {
            canvasGroup.alpha = 0;
        }
    }
}
