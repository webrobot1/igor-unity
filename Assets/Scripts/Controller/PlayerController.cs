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
    public class PlayerController : UpdateController
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

        public Image hpFrame;
        public Image mpFrame;

        public static PlayerController Instance { get; private set; }
        protected new void Awake()
        {
            // If there is an instance, and it's not me, delete myself.
            if (hpFrame == null)
                Error("не присвоен GameObject для линии жизни");

            if (mpFrame == null)
                Error("не присвоен GameObject для линии маны");

            if (Instance != null && Instance != this)
            {
                Destroy(this);
            }
            else
            {
                Instance = this;
            }

            base.Awake();
        }

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

        private void Update()
        {
            if (player != null && loading == null)
            {
                Vector2Int moveTo = Vector2Int.zero;

                // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
                if (Input.GetMouseButtonDown(0))
                {
                    moveTo = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
                }

                if (Input.GetKeyDown("space"))
                {
                    Response response = new Response();
                   // response.action = "attack/index";
                    response.action = "firebolt/index";
                    base.Send(response);
                }

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
                    else 
                    {
                        response.action = "move/to";
                        response.x = moveTo.x;
                        response.y = moveTo.y;
                        response.z = (int)player.transform.position.z;
                    }

                    base.Send(response);
                }
            }
        }

        // Update is called once per frame
        protected override void FixedUpdate()
        {
            // это позволит нам при декодировании ответа использовать новые структуры игроков, врагов и обектов где по сути из нового  - компоненты
            base.HandleData<NewPlayerRecive, NewEnemyRecive, NewObjectRecive>();
        }
    }
}