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
    public class ActionBarController : MonoBehaviour, IPointerClickHandler
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
            // на сервере есть првоерка на то можем ли мы стрелять, но что бы не сдать впустую запрос который никчему не приведет  - ограничим и тут
            if (_icon!= null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp>0)
            {
                Debug.LogError(_icon);

                AttackResponse response = new AttackResponse();
                response.magic = "firebolt";

                if (PlayerController.Target != null)
                {
                    response.target = PlayerController.Target.key;
                }
                else
                {
                    // именно то в каком положении наш персонаж
                    response.x = Math.Round(PlayerController.Player.transform.forward.x, PlayerController.position_precision);
                    response.y = Math.Round(PlayerController.Player.transform.forward.y, PlayerController.position_precision);
                }
                response.Send();
            }
        }
    }
}