using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Единственная точка чтения указателя и осей движения игроком. Живая игра берёт через неё системный
    /// ввод — ответы те же, что у <see cref="Input"/> напрямую.
    ///
    /// Ради чего заведена: в редакторе ввод перехватывает оснастка съёмки роликов
    /// (Assets/Plugins/Mmogick/VideoRig) — указатель ведёт и нажимает сценарий, а не человек. Перехват
    /// обязан быть ОДНОЙ точкой: положение указателя игра читает в десятке мест (сама рука, подсветка
    /// наведения, луч клика, надпись над сущностью), и разведённое по ним условие рано или поздно
    /// разойдётся — часть мест осталась бы на системной мыши, и снятое разошлось бы с тем, что видит
    /// игрок.
    ///
    /// В сборку игрока перехват не попадает вовсе: и состояние сценария, и его методы лежат под
    /// UNITY_EDITOR, а свойства чтения сводятся к прямому обращению к <see cref="Input"/>.
    /// </summary>
    public static class InputSource
    {
#if UNITY_EDITOR
        /// <summary>
        /// Идёт ли сценарий съёмки. Снят — весь ввод берётся у системы, поведение игры ровно прежнее.
        /// </summary>
        public static bool Scripted;

        private static Vector3 pointer;

        private static float axisHorizontal;

        private static float axisVertical;

        /// <summary>
        /// Кадр, в котором нажатие видно как «нажали именно сейчас», и кадр отпускания. Сценарий
        /// исполняется корутиной — она идёт ПОСЛЕ Update всех компонентов, поэтому нажатие, заявленное
        /// сценарием, показываем со СЛЕДУЮЩЕГО кадра: иначе читатели, отработавшие раньше в этом же
        /// кадре, его не увидят вовсе.
        /// </summary>
        private static int downFrame = -1;

        private static int upFrame = -1;

        private static bool held;

        /// <summary>Указатель, рисуемый сейчас в кадре. Пусто — указателя в кадре нет.</summary>
        private static Sprite pointerSprite;

        /// <summary>
        /// Картинка руки курсора, собранная под кадр. Живёт между показами: сценарий показывает и прячет
        /// руку у каждого своего ведения, а пересборка на каждый показ оставляла бы за прогон десятки
        /// мёртвых объектов. Статика переживает остановку игры, а созданный ею спрайт — нет, поэтому
        /// живость проверяем Unity-сравнением на каждой выдаче.
        /// </summary>
        private static Sprite hand;

        /// <summary>Размер указателя в пикселях кадра и точка картинки, которой он попадает в экран.</summary>
        private static Vector2 pointerSize;

        private static Vector2 pointerPivot;

        /// <summary>Виден ли указатель сценария в кадре.</summary>
        public static bool PointerShown
        {
            get { return pointerSprite != null; }
        }

        /// <summary>
        /// Начать перехват. Указателя в кадре при этом нет: показывает и прячет его сам сценарий по ходу
        /// прогона (<see cref="ShowPointer"/>, <see cref="HidePointer"/>).
        /// </summary>
        public static void BeginScript(Vector3 startPointer)
        {
            pointerSprite = null;
            pointer = startPointer;
            axisHorizontal = 0;
            axisVertical = 0;
            downFrame = -1;
            upFrame = -1;
            held = false;
            Scripted = true;
        }

        /// <summary>
        /// Показать указатель в кадре: системный курсор в запись не попадает — его рисует система поверх
        /// картинки окна, — поэтому руку рисует сама игра рукой курсора. <paramref name="scale"/> — во
        /// сколько раз она крупнее системной: размер кадра знает снимающий, не игра. Отказ (картинки
        /// указателя нет) возвращается признаком: без руки прогон снимает не то, что заказан.
        /// </summary>
        public static bool ShowPointer(float scale)
        {
            Texture2D texture = HandCursor.OpenTexture;

            if (texture == null)
                return false;

            if (hand == null)
                hand = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            // Точка попадания у картинки своя (у ладони — между пальцами), а положение рисуемой руки
            // задаётся её точкой опоры: переводим одну в другую. Отсчёт точки попадания идёт сверху, у
            // точки опоры — снизу.
            pointerPivot = new Vector2(
                HandCursor.OpenHotspot.x / texture.width,
                1f - HandCursor.OpenHotspot.y / texture.height);

            pointerSize = new Vector2(texture.width, texture.height) * scale;
            pointerSprite = hand;
            return true;
        }

        /// <summary>
        /// Убрать указатель из кадра. Гасит картинку <see cref="DrawPointer"/> следующим кадром: рисует
        /// её игра, ей же и снимать — снаружи до картинки курсора не дотянуться.
        /// </summary>
        public static void HidePointer()
        {
            pointerSprite = null;
        }

        public static void EndScript()
        {
            Scripted = false;
            held = false;
            pointerSprite = null;
        }

        public static void SetPointer(Vector3 screen)
        {
            pointer = screen;
        }

        public static void SetAxis(float horizontal, float vertical)
        {
            axisHorizontal = horizontal;
            axisVertical = vertical;
        }

        public static void PressMouse()
        {
            downFrame = Time.frameCount + 1;
            held = true;
        }

        public static void ReleaseMouse()
        {
            upFrame = Time.frameCount + 1;
            held = false;
        }

        /// <summary>
        /// Нарисовать указатель сценария рукой курсора либо убрать его из кадра — сценарий показывает
        /// руку лишь на время действия, которое ею пользуется. Пока в руке несут предмет, картинку
        /// держит сам курсор — её и оставляем: что именно в руке, важнее формы указателя.
        /// </summary>
        public static void DrawPointer(Image cursor)
        {
            if (!Scripted || CursorController.MyMoveable != null)
                return;

            if (pointerSprite == null)
            {
                // Гасим тем же, чем гасит картинку курсора сама игра при пустой руке: спрайт остаётся,
                // невидим цвет.
                cursor.color = new Color(0, 0, 0, 0);
                return;
            }

            cursor.sprite = pointerSprite;
            cursor.color = Color.white;
            cursor.preserveAspect = true;

            RectTransform rect = (RectTransform)cursor.transform;

            // Положение руке задают пикселями экрана, а размер она держит в единицах холста — тот сжат
            // под разрешение окна. Без деления на его масштаб указатель менялся бы в размере вместе с
            // размером кадра. Масштаб самой руки сбрасываем: его ставит поднятый в неё предмет.
            Canvas canvas = cursor.canvas;
            float scale = canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;

            rect.pivot = pointerPivot;
            rect.sizeDelta = pointerSize / scale;
            rect.localScale = Vector3.one;
        }
#endif

        public static Vector3 MousePosition
        {
            get
            {
#if UNITY_EDITOR
                if (Scripted)
                    return pointer;
#endif
                return Input.mousePosition;
            }
        }

        /// <summary>Нажали в этом кадре.</summary>
        public static bool MouseDown
        {
            get
            {
#if UNITY_EDITOR
                if (Scripted)
                    return Time.frameCount == downFrame;
#endif
                return Input.GetMouseButtonDown(0);
            }
        }

        /// <summary>Держат нажатой.</summary>
        public static bool MouseHeld
        {
            get
            {
#if UNITY_EDITOR
                if (Scripted)
                    return held;
#endif
                return Input.GetMouseButton(0);
            }
        }

        /// <summary>Отпустили в этом кадре.</summary>
        public static bool MouseUp
        {
            get
            {
#if UNITY_EDITOR
                if (Scripted)
                    return Time.frameCount == upFrame;
#endif
                return Input.GetMouseButtonUp(0);
            }
        }

        public static float GetAxis(string axis)
        {
#if UNITY_EDITOR
            if (Scripted)
                return axis == "Horizontal" ? axisHorizontal : axisVertical;
#endif
            return Input.GetAxis(axis);
        }
    }
}
