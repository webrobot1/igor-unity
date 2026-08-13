using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    /// <summary>
    /// Ответ клавишей Enter для поля ввода: UI-система шлёт выделенному полю событие подтверждения, но
    /// само поле лишь закрывает правку — вопрос, ради которого поле открыли, остаётся без ответа. Реле
    /// вешается на тот же объект и передаёт подтверждение владельцу окна.
    ///
    /// Событие завершения правки (onEndEdit) для этого не годится: оно приходит и когда игрок просто
    /// щёлкнул мимо поля, а уход мышью ответом не является.
    /// </summary>
    public class SubmitRelay : MonoBehaviour, ISubmitHandler
    {
        /// <summary>Кого звать по Enter. Назначает окно-владелец при настройке поля.</summary>
        public Action Submitted;

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (Submitted != null)
                Submitted();
        }
    }
}
