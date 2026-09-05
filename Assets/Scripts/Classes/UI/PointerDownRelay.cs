using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick
{
    /// <summary>
    /// Нажатие указателем — палец лёг на элемент интерфейса либо кнопка мыши нажата над ним: UI-система
    /// шлёт его самому элементу, а распорядиться нажатием нужно владельцу. Реле вешается на тот же объект
    /// и передаёт нажатие ему.
    ///
    /// Собственный обработчик элемента при этом продолжает работать: UI-система зовёт все обработчики
    /// нажатия, какие на объекте есть, — потому реле годится и для элемента, код которого нам не
    /// принадлежит (сторонний пакет).
    ///
    /// Отпускание и ведение реле не передаёт: нажатие — момент, а не состояние; кому нужно ведение, тот
    /// читает само значение элемента.
    /// </summary>
    public class PointerDownRelay : MonoBehaviour, IPointerDownHandler
    {
        /// <summary>Кого звать по нажатию. Назначает владелец при настройке элемента.</summary>
        public Action Pressed;

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            if (Pressed != null)
                Pressed();
        }
    }
}
