using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Пункт списка в окне сведений: значок слева, текст справа. Нужен ради переноса — пункт бывает
    /// длиннее строки, и его продолжение должно вставать под текстом, а не под значком, иначе соседние
    /// пункты сливаются в сплошной абзац. Держит раскладку сам префаб, класс лишь даёт окну ссылку на
    /// текст: значок и текст — разные объекты, и по одному компоненту до нужного не добраться.
    /// </summary>
    public class InfoPoint : MonoBehaviour
    {
        [SerializeField]
        private Text text;

        public string Text
        {
            set
            {
                if (text == null)
                {
                    ConnectController.Error("в префабе пункта списка не указан его текст");
                    return;
                }

                text.text = value;
            }
        }
    }
}
