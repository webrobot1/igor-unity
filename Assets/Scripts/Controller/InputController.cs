using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MyFantasy
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class InputController : PlayerController
    {
        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        private VariableJoystick joystick;

        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        private Button button_attack;      

        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private double horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private double vertical;

        protected override void Awake()
        {
            // продолжать принимать данные и обновляться в фоновом режиме
            Application.runInBackground = true;

            if (joystick == null)
                Error("не указан джойстик");

            if (button_attack == null)
                Error("не указана кнопка атаки");

            button_attack.onClick.AddListener(Attack);

            joystick.SnapX = true;
            joystick.SnapY = true;

           #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.unityLogger.logEnabled = true;
            #else
               Debug.unityLogger.logEnabled = false;
            #endif

            base.Awake();
        }

        private void Attack()
        {
            Response response = new Response();
            // response.action = "attack/index";
            response.group = "firebolt";
            base.Send(response);
        }

        protected override void Update() 
        {
            base.Update();
            if (player != null)
            {
                try
                {
                    Vector2Int move_to = Vector2Int.zero;

                    // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
                    if (Input.GetMouseButtonDown(0))
                    {
                        ObjectModel enemy;
                        if (EventSystem.current.IsPointerOverGameObject() || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)))
                        {
                            Debug.Log("Clicked the UI");
                            if (enemy = UnityEngine.EventSystems.EventSystem.current.GetComponent<ObjectModel>())
                            {
                                Debug.Log("Кликнули на враг "+ enemy.key);
                            }
                        }
                        else
                        {
                            move_to = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
                            if (move_to == new Vector2Int((int)player.transform.position.x, (int)player.transform.position.y))
                                move_to = Vector2Int.zero;
                        }
                    }

                    if (Input.GetKeyDown("space"))
                    {
                        Response response = new Response();
                        // response.action = "attack/index";
                        response.group = "firebolt";
                        base.Send(response);
                    }

                    vertical = Input.GetAxisRaw("Vertical") != 0 ? Input.GetAxisRaw("Vertical") : joystick.Vertical;
                    horizontal = Input.GetAxisRaw("Horizontal") != 0 ? Input.GetAxisRaw("Horizontal") : joystick.Horizontal;
 
                    // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                    // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                    if (
                        (
                            vertical != 0
                                ||
                            horizontal != 0
                                ||
                            move_to != Vector2Int.zero
                        )
                          /*  &&
                        (
                           player.GetEventRemain("side") <= 0
                        )*/
                    )
                    {
                        MoveResponse response = new MoveResponse();
                        response.group = "move";

                        if (vertical != 0 || horizontal != 0)
                        {
                            string side = player.side;

                            if (vertical > 0 && horizontal > 0)
                            {
                                response.action = "up_right";
                            }
                            else if (vertical > 0 && horizontal < 0)
                            {
                                response.action = "up_left";
                            }
                            else if (vertical < 0 && horizontal > 0)
                            {
                                response.action = "down_right";
                            }
                            else if (vertical < 0 && horizontal < 0)
                            {
                                response.action = "down_left";
                            }
                            else if (vertical > 0)
                            {
                                response.action = "up";
                            }
                            else if (vertical < 0)
                            {
                                response.action = "down";
                            }                           
                            else if (horizontal > 0)
                            {
                                response.action = "right";
                            }
                            else if (horizontal < 0)
                            {
                                response.action = "left";
                            }
                        }
                        else
                        {
                            response.action = "to";
                            response.x = move_to.x;
                            response.y = move_to.y;
                            response.z = (int)player.transform.position.z;

                            move_to = Vector2Int.zero;
                        }

                        base.Send(response);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error("Ошибка управелния игроком: "+ex.Message);
                }
            }
        }
    }
}