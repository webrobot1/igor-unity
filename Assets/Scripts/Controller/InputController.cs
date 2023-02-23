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


        [SerializeField]
        private Button button_skill1;              
        
        [SerializeField]
        private Button button_skill2;       
        
        [SerializeField]
        private Button button_skill3;      

        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private float horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private float vertical;

        protected override void Awake()
        {
            // продолжать принимать данные и обновляться в фоновом режиме
            Application.runInBackground = true;

            if (joystick == null)
                Error("не указан джойстик");

            if (button_skill1 == null)
                Error("не указана кнопка атаки 1");          
            
            if (button_skill2 == null)
                Error("не указана кнопка атаки 2");

            if (button_skill3 == null)
                Error("не указана кнопка атаки 2");

            button_skill1.onClick.AddListener(Attack);
            button_skill2.onClick.AddListener(Attack);
            button_skill3.onClick.AddListener(Attack);

           #if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.unityLogger.logEnabled = true;
            #else
               Debug.unityLogger.logEnabled = false;
            #endif

            base.Awake();
        }

        private void Attack()
        {
            BoltResponse response = new BoltResponse();
            // response.group = "attack";
            response.group = "bolt";
            response.action = "to";

            response.prefab = "icebolt";
            response.key = "enemys_1";

            // response.x = Math.Round(player.forward.x, 1);
            // response.y = Math.Round(player.forward.y, 1);

            base.Send(response);
        }

        protected override void Update() 
        {
            base.Update();
            if (player != null)
            {
                try
                {
                    Vector3 move_to = Vector3Int.zero;

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
                            move_to = GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition);
                           
                            if (Vector3.Distance(player.position, move_to) < 1.15f) 
                                move_to = Vector3.zero;
                        }
                    }

                    if (Input.GetKeyDown("space"))
                    {
                        Attack();
                    }

                    vertical = Input.GetAxis("Vertical") != 0 ? Input.GetAxis("Vertical") : joystick.Vertical;
                    horizontal = Input.GetAxis("Horizontal") != 0 ? Input.GetAxis("Horizontal") : joystick.Horizontal;
 
                    // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                    // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                    if (
                        (
                            vertical != 0
                                ||
                            horizontal != 0
                                ||
                            move_to != Vector3.zero
                        )
                    )
                    {
                        if (vertical != 0 || horizontal != 0)
                        {
                            player.forward = new Vector3(horizontal, vertical, 0);

                            // я подогнал магнитуду под размер круга джойстика (выйдя за него мы уже будем идти а не менять направления)
                            if ((new Vector2(horizontal, vertical)).magnitude > 0.5)
                            {
                                MoveResponse response = new MoveResponse();

                                response.group = "move";
                                response.x = Math.Round(player.forward.x, 1);
                                response.y = Math.Round(player.forward.y, 1);

                                base.Send(response);
                            }  
                        }
                        else
                        {
                            MoveResponse response = new MoveResponse();
                            
                            response.action = "to";
                            response.group = "move";
                            response.x = Math.Round(move_to.x, 1);
                            response.y = Math.Round(move_to.y, 1);
                            response.z = player.transform.position.z;

                            move_to = Vector3.zero;
                            base.Send(response);
                        } 
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Error("Ошибка управелния игроком: "+ex.Message);
                }
            }
        }

        #if !UNITY_EDITOR && (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS)
		    // повторная загрузка всего пира по новой при переключении между вкладками браузера
		    // если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
		    // TODO придумать как отказаться от этого
		    private void Load()
		    {
                if (player != null)
                {
			        Response response = new Response();
			        response.group = "load";

			        Send(response);
                }
		    }
        #endif

        #if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
		    public void OnApplicationPause(bool pause)
		    {
			    Debug.Log("Пауза " + pause);
			    Load();
		    }
        #endif

        #if UNITY_WEBGL && !UNITY_EDITOR
		    public void OnApplicationFocus(bool focus)
		    {
			    Debug.Log("фокус " + focus);
			    Load();
		    }
        #endif
    }
}