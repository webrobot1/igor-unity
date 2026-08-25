#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    /// <summary>
    /// Ввод для EventSystem на время съёмки ролика. Оснастка съёмки (Assets/Plugins/Mmogick/VideoRig)
    /// ставит его в <c>BaseInputModule.inputOverride</c>, и с этого момента интерфейс — кнопки окон,
    /// слоты панели, наведение с подсказкой — получает нажатия оттуда же, откуда их берёт игровой код,
    /// из <see cref="InputSource"/>. Без этого нажатие сценария пришлось бы разыгрывать вызовом
    /// обработчиков в обход модуля, и снятое разошлось бы с тем, что видит игрок: модуль ведёт своё
    /// состояние — нажатый объект, перетаскивание, вход и выход указателя.
    ///
    /// Лежит в подкаталоге Runtime оснастки, в отдельной от неё рантайм-сборке, по требованию движка:
    /// компонент объекта сцены ищется по скрипту рантайм-сборки, и добавление редакторного класса
    /// возвращает пустоту. В сборку игрока не попадает — файл целиком под UNITY_EDITOR.
    ///
    /// Замещение полное, вклад базового класса не зовём осознанно: он читает системный ввод, а на время
    /// сценария живой ввод не участвует вовсе — иначе движения руки человека попадали бы в запись.
    /// Кнопки, кроме левой, сценарием не нажимаются: игра других не читает.
    /// </summary>
    public class ScenarioPointerInput : BaseInput
    {
        public override Vector2 mousePosition
        {
            get { return InputSource.MousePosition; }
        }

        public override bool mousePresent
        {
            get { return true; }
        }

        public override bool touchSupported
        {
            get { return false; }
        }

        public override int touchCount
        {
            get { return 0; }
        }

        public override Vector2 mouseScrollDelta
        {
            get { return Vector2.zero; }
        }

        public override bool GetMouseButtonDown(int button)
        {
            return button == 0 && InputSource.MouseDown;
        }

        public override bool GetMouseButton(int button)
        {
            return button == 0 && InputSource.MouseHeld;
        }

        public override bool GetMouseButtonUp(int button)
        {
            return button == 0 && InputSource.MouseUp;
        }
    }
}
#endif
