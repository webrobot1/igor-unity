using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using WebGLSupport;

namespace MyFantasy
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class ActionBar : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        private Image _icon;

        public Image Icon 
        { 
            get => _icon;
            set => _icon = value; 
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            
        }
    }
}