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
    public class Spell : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        private Image image;

        public Text title;
        public string group;
        public Text description;

        public Text mp;
        public Text remain;

        private string _magic;

        public string Magic
        {
            get
            {
                return _magic;
            }
            set
            {
                _magic = value;

                Sprite sprite = Resources.Load<Sprite>("Sprites/Spells/" + value);
                if (sprite == null)
                {
                    Debug.LogError("не найдено изображение заклинания с сервера " + value);
                    sprite = Resources.Load<Sprite>("Sprites/Spells/unknow");
                }

                image.sprite = sprite;
            }
        }

        protected void Awake()
        {
            if (image == null)
                ConnectController.Error("не найден объект sprite в для элемента Заклинания в книге");

            if (title == null)
                ConnectController.Error("не найден объект title в для элемента Заклинания в книге");

            if (description == null)
                ConnectController.Error("не найден объект description в для элемента Заклинания в книге");

            if (mp == null)
                ConnectController.Error("не найден объект mana в для элемента Заклинания в книге");
        }

        protected void FixedUpdate()
        {
            if (PlayerController.Player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
            {
                double remainTime = PlayerController.Player.GetEventRemain(group);
                if(remainTime > 0)
                    remain.text = remainTime + " сек.";
                else
                    remain.text = "0 сек.";
            } 
        }

        private Response? cast()
        {
            switch (group)
            {
                case "fight/attack":
                    AttackResponse response = new AttackResponse();
                    response.magic = Magic;

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

                    return response;
                default:
                    ConnectController.Error("неизвестный тип группы "+ group+" у заклинания "+Magic);
                break;
            }

            return null;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            // на сервере есть првоерка на то можем ли мы стрелять, но что бы не сдать впустую запрос который никчему не приведет  - ограничим и тут
            if (PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
            {
                Debug.LogError(Magic);

                Response response = cast();
                response.Send();
            }
        }
    }
}