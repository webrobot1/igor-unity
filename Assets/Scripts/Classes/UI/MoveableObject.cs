﻿using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyFantasy
{
    public abstract class MoveableObject : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        protected Image image;

        /// <summary>
        /// Нужен для курсора (перетаскивание картинки предмета)
        /// </summary>
        public Image Image
        {
            get { return image; }
            set { }
        }

        /// <summary>
        /// Это вызывается и при перетаскивании и при вызове (и из книги/инвентаря и из быстрого доступа)
        /// </summary>
        /// <param name="gameObject">
        /// На какой объект перетаскивается (из быстрого меню просто null)
        /// </param>
        public abstract void Use(GameObject gameObject = null);

        protected virtual void Awake()
        {
            if (image == null)
                ConnectController.Error("не найден объект sprite в для элемента Заклинания в книге");
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            CursorController.TakeMoveable(this);
        }
    }
}
