using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using WebGLSupport;

namespace Mmogick
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class Spell: MoveableObject, IPointerClickHandler
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
                {
                    remain.text = remainTime + " сек.";
                    image.raycastTarget = false;
                }
                else
                {
                    image.raycastTarget = true;
                    remain.text = "0 сек.";
                }
                   

                if (Int32.Parse(mp.text) > PlayerController.Player.mp)
                {
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 0.5f);
                    image.raycastTarget = false;
                }
                else
                {
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 1f);
                    image.raycastTarget = true;
                }
            } 
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (Int32.Parse(mp.text) <= PlayerController.Player.mp && PlayerController.Player.GetEventRemain(group)<=0)
            {
                CursorController.TakeMoveable(this);
            }
        }

        public override void Use(Vector2 pos = new Vector2(), GameObject obj = null)
        {
            if(obj != null && obj.GetComponent<ActionBar>())
            {
                ActionBar bar = obj.GetComponent<ActionBar>();
                ActionBarsResponse response = new ActionBarsResponse();

                if (bar.Item != this)
                {
                    Debug.Log("Быстрая клавиша " + bar.num + ": отправим на сервер установку заклинания " + Magic);
                    response.actionbars.Add(bar.num, new ActionBarsRecive("spell", Magic));
                }
                else
                {
                    Debug.LogWarning("Быстрая клавиша " + bar.num + ": Попытка установить одинаковые значение - очищаем ячейку");
                    response.actionbars.Add(bar.num, null);
                }
                response.Send();
            }
            else
            {
                if(Int32.Parse(mp.text) <= PlayerController.Player.mp)
                {
                    Debug.Log("Используем заклинание "+ Magic);
                    switch (group)
                    {
                        case "fight/attack":
                            AttackResponse response = new AttackResponse();
                            response.magic = Magic;

                            if (obj != null && obj.GetComponent<ObjectModel>()!=null)
                            {
                                response.target = obj.GetComponent<ObjectModel>().key;                      
                                if(MainController.Instance.Target == null)
                                    MainController.Instance.Target = gameObject.GetComponent<ObjectModel>();
                            }
                            else if (MainController.Instance.Target != null)
                            {
                                response.target = MainController.Instance.Target.key;
                            }
                            else if(pos != Vector2.zero)
                            {
                                PlayerController.Player.Forward = new Vector3(pos.x, pos.y, PlayerController.Player.Forward.z);

                                // именно то в каком положении наш персонаж
                                response.x = Math.Round(pos.x, PlayerController.position_precision);
                                response.y = Math.Round(pos.y, PlayerController.position_precision);
                            }

                            response.Send();
                        break;
                        default:
                            ConnectController.Error("неизвестный тип группы "+ group+" у заклинания "+Magic);
                        break;
                    }
                }
                else
                    Debug.LogError("Недостаточно маны для заклинания " + Magic);
            }
        }
    }
}