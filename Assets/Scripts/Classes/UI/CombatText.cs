using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    public class CombatText : MonoBehaviour
    {
        [SerializeField]
        private float speed = 1f;

        [SerializeField]
        private float lifeTime = 1.5f;

        private Text text;

        // Чья это надпись и когда появилась — по ним CombatTextController разводит соседние во времени надписи
        // одной сущности, чтобы они не летели одна в другой.
        public Transform Owner { get; private set; }
        public float Born { get; private set; }

        public void Init(Transform owner)
        {
            Owner = owner;
            Born = Time.time;
        }

        void Start()
        {
            text = GetComponentInChildren<Text>();
            StartCoroutine(FadeOut());
        }

        void Update()
        {
            transform.Translate(Vector2.up * speed * Time.deltaTime);
        }

        private IEnumerator FadeOut()
        {
            float startAlpha = text != null ? text.color.a : 1f;
            float rate = 1.0f / lifeTime;
            float progress = 0.0f;

            while (progress < 1.0f)
            {
                if (text != null)
                {
                    Color tmp = text.color;
                    tmp.a = Mathf.Lerp(startAlpha, 0, progress);
                    text.color = tmp;
                }

                progress += rate * Time.deltaTime;
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
