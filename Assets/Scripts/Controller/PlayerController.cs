using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyFantasy
{
    /// <summary>
    /// Класс для отправки данных (действий игрока)
    /// </summary>
    public class PlayerController : ConnectController
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

        private Vector2Int moveTo = Vector2Int.zero;

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

        protected new Recive GetReciveStruct()
        {
            return new NewRecive();
        }

        private void Update()
        {
            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                moveTo = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
                Debug.Log(moveTo);
            }
        }

        // Update is called once per frame
        private void FixedUpdate()
        {
            base.FixedUpdate();

            if (playerModel != null && connect!=null && connect.pause==null)
            {
                // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                if (
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
                {
                
                    MoveResponse response = new MoveResponse();

                    if (vertical != 0 || horizontal != 0)
                    {
                        if (vertical > 0)
                        {
                            response.action = "move/up";
                        }
                        else if (vertical < 0)
                        {
                            response.action = "move/down";
                        }
                        else if (horizontal > 0)
                        {
                            response.action = "move/right";
                        }
                        else if (horizontal < 0)
                        {
                            response.action = "move/left";
                        }
                    }
                    else if (Vector2.Distance(playerModel.transform.position, moveTo) >= 1)
                    {
                        response.action = "move/to";
                        response.x = moveTo.x;
                        response.y = moveTo.y;
                        response.z = (int)playerModel.transform.position.z;

                        // очищаем переменную после того как команда отправлена (дальше сам пойдет)
                        moveTo = Vector2Int.zero;
                    }
                    else
                    {
                        return;
                    }

                    connect.Send(response);
                }
            }
        }
    }
}