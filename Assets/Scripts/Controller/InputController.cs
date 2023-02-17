using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
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
        private VariableJoystick variableJoystick;      

        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private double horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private double vertical;

        private void Start()
        {
            // продолжать принимать данные и обновляться в фоновом режиме
            Application.runInBackground = true;

            // наш джойстик
            variableJoystick = GameObject.Find("joystick").GetComponent<VariableJoystick>();
            variableJoystick.SnapX = true;
            variableJoystick.SnapY = true;

           #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.unityLogger.logEnabled = true;
            #else
               Debug.unityLogger.logEnabled = false;
            #endif
        }

        protected override void Update()
        {
            base.Update();
            if (player != null)
            {
                try
                {          
                    Vector2Int moveTo = Vector2Int.zero;

                    // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
                    if (Input.GetMouseButtonDown(0))
                    {
                        moveTo = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
                        if(moveTo == new Vector2Int((int)player.transform.position.x, (int)player.transform.position.y))
                            moveTo = Vector2Int.zero;
                    }

                    if (Input.GetKeyDown("space"))
                    {
                        Response response = new Response();
                        // response.action = "attack/index";
                        response.group = "firebolt";
                        base.Send(response);
                    }

                    // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                    // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                    if (
                        (
                            (vertical = Input.GetAxis("Vertical")) != 0
                                 ||
                            (vertical = variableJoystick.Vertical) != 0
                                 ||
                            (horizontal = Input.GetAxis("Horizontal")) != 0
                                ||
                            (horizontal = variableJoystick.Horizontal) != 0
                                ||
                            moveTo != Vector2Int.zero
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
                            if (vertical > 0 && horizontal == 0)
                            {
                                response.action = "up";
                            }
                            else if (vertical < 0 && horizontal == 0)
                            {
                                response.action = "down";
                            }                           
                            else if (horizontal > 0 && vertical == 0)
                            {
                                response.action = "right";
                            }
                            else if (horizontal < 0 && vertical == 0)
                            {
                                response.action = "left";
                            }
                            else if (vertical > 0 && horizontal > 0)
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
                        }
                        else
                        {
                            response.action = "to";
                            response.x = moveTo.x;
                            response.y = moveTo.y;
                            response.z = (int)player.transform.position.z;
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
            else
                Debug.LogWarning("Ждем присвоения игрока");
        }
    }
}