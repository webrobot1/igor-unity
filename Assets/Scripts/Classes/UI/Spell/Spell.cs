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
    public class Spell: MoveableObject
    {
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


        protected override void Awake()
        {
            if (title == null)
                ConnectController.Error("не найден объект title в для элемента Заклинания в книге");

            if (description == null)
                ConnectController.Error("не найден объект description в для элемента Заклинания в книге");

            if (mp == null)
                ConnectController.Error("не найден объект mana в для элемента Заклинания в книге");

            base.Awake();
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

        public override void Use(GameObject gameObject = null)
        {
            if(gameObject!=null && gameObject.GetComponent<ActionBar>())
            {
                int num = gameObject.GetComponent<ActionBar>().num;

                Debug.LogWarning("Быстрая клавиша " + num + ": отправим на сервер установку заклинания " + Magic);
                ActionBar bar = Array.Find(UIController.Instance.ActionBars, element => element.num == num);

                ActionBarsResponse response = new ActionBarsResponse();

                response.actionbars.Add(bar.num, new ActionBarsRecive("spell", Magic));
                response.Send();
            }
            else
            {
                Debug.Log("Используем заклинание "+ Magic);
                switch (group)
                {
                    case "fight/attack":
                        AttackResponse response = new AttackResponse();
                        response.magic = Magic;

                        if (gameObject != null && gameObject.GetComponent<ObjectModel>())
                        {
                            response.target = gameObject.GetComponent<ObjectModel>().key;
                           
                            if(UIController.Instance.Target == null)
                                UIController.Instance.Target = gameObject.GetComponent<ObjectModel>();
                        }
                        else if (UIController.Instance.Target != null)
                        {
                            response.target = UIController.Instance.Target.key;
                        }
                        else
                        {
                            // именно то в каком положении наш персонаж
                            response.x = Math.Round(PlayerController.Player.Forward.x, PlayerController.position_precision);
                            response.y = Math.Round(PlayerController.Player.Forward.y, PlayerController.position_precision);
                        }

                        response.Send();
                    break;
                    default:
                        ConnectController.Error("неизвестный тип группы "+ group+" у заклинания "+Magic);
                    break;
                }
            }
        }
    }
}