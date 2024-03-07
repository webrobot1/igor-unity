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
    /// Класс для управления всеми UI эллементами на экране и передачи в них инфомрацию с сервера
    /// </summary>
    public class UIController : PlayerController
    {
        /// <summary>
        /// книга заклинаний
        /// </summary>
        [SerializeField]
        private CanvasGroup settingGroup;        
        
        /// <summary>
        /// книга заклинаний
        /// </summary>
        [SerializeField]
        private CanvasGroup spellBook;

        /// <summary>
        /// группа всех меню
        /// </summary>
        [SerializeField]
        private GameObject menuGroup;

        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private float horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private float vertical;

        private Vector3 move_to = Vector3.zero;

        /// <summary>
        /// если мы стреляем и продолжаем идти заблокируем поворот (он без запроса к серверу делется) в сторону хотьбы (а то спиной стреляем)
        /// </summary>
        private DateTime block_forward = DateTime.Now;

        protected override void Awake()
        {
            CloseAllMenu();
            base.Awake();
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            // если с сервера пришла анимация заблокируем повороты вокруг себя на какое то время (а то спиной стреляем идя и стреляя)
            if (player != null && key == player.key && recive.action!=null)
            {
                block_forward = DateTime.Now.AddSeconds(0.2f);
            }

            return base.UpdateObject(map_id, key, recive, type);
        }

        protected override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.P))
            {
                OpenClose(spellBook);
            }            
            
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OpenClose(settingGroup);
            }

            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                // кликнули на ГШ эллемент
                if
                (
                    (EventSystem.current.IsPointerOverGameObject() || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)))
                        &&
                    EventSystem.current.GetComponentInParent<EntityModel>() == null
                )
                {
                    Debug.Log("Clicked the UI");
                }
                else
                {
                    RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero, Mathf.Infinity);
                   
                    // кликнули на какой то объект
                    if (hit.collider != null)
                    {
                        ObjectModel new_target = hit.collider.GetComponent<ObjectModel>();
                        if (new_target != null)
                        {
                            Target = new_target;
                            persist_target = true;
                            Debug.Log("Кликнули на " + new_target.key);
                        }
                    }
                    else
                    {
                        Target = null;
                        persist_target = false;
                        Debug.Log("Кликнули на "+Camera.main.ScreenToWorldPoint(Input.mousePosition));

                        // движение к указанной клетке
                        if (player != null) 
                        { 
                            move_to = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                            if (Vector3.Distance(player.position, move_to) < 1.15f)
                            move_to = Vector3.zero;
                        }
                    }
                }
            }
        }

        public void OpenClose(CanvasGroup canvasGroup)
        {
            // закроем все меню
            CloseAllMenu(canvasGroup);

            // не только скрыть но и позволить кликать по той области что бы ходить персонажем
            canvasGroup.alpha = canvasGroup.alpha>0?0:1;
            canvasGroup.blocksRaycasts = canvasGroup.blocksRaycasts ?false:true;
        }

        private void CloseAllMenu(CanvasGroup canvasGroup = null)
        {
            if (menuGroup == null)
                Error("не присвоена группа объектов меню объекту UI");

            // закроем все меню
            foreach (Transform child in menuGroup.transform)
            {
                if (canvasGroup != null && canvasGroup == child.GetComponent<CanvasGroup>())
                    continue;

                child.GetComponent<CanvasGroup>().alpha = 0;
                child.GetComponent<CanvasGroup>().blocksRaycasts = false;
            }
        }

        protected override void FixedUpdate() 
        {
            base.FixedUpdate();
            if (player != null && player.action != ACTION_REMOVE)
            {
                try
                {
                    vertical = Input.GetAxis("Vertical") != 0 ? Input.GetAxis("Vertical") : joystick.Vertical;
                    horizontal = Input.GetAxis("Horizontal") != 0 ? Input.GetAxis("Horizontal") : joystick.Horizontal;
 
                    // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
                    // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
                    if (
                        (
                            move_to != Vector3.zero
                                 ||
                            vertical != 0
                                ||
                            horizontal != 0
                        )
                    )
                    {
                        if (vertical != 0 || horizontal != 0)
                        {
                            // я подогнал магнитуду под размер круга джойстика (выйдя за него мы уже будем идти а не менять направления)
                            if (Math.Abs(horizontal) > 0.5 || Math.Abs(vertical) > 0.5)
                            {
                                // не путать импульс нажатия кнопки в определенном направлении с forward (направлением движения, т.е нормальизованным вектором)
                                Vector3 vector = new Vector3(horizontal, vertical, 0).normalized;

                                // значение forward не сменится (тк его меняет только сервер) но запустится анимация при которой графика персонажа повернется
                                //if (DateTime.Compare(block_forward, DateTime.Now) < 1)
                                 //   player.forward = vector;

                                WalkResponse response = new WalkResponse();

                                response.x = Math.Round(vector.x, position_precision);
                                response.y = Math.Round(vector.y, position_precision);
                                response.Send();
                            }  
                        }
                        else
                        {
                            WalkResponse response = new WalkResponse();
                            
                            response.action = "to";
                            response.x = Math.Round(move_to.x, position_precision);
                            response.y = Math.Round(move_to.y, position_precision);
                            response.z = player.transform.position.z;
                            response.Send();

                            move_to = Vector3.zero;
                        } 
                    }
                }
                catch (Exception ex)
                {
                    Error("Ошибка управелния игроком: ", ex);
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
			        LoadResponse response = new LoadResponse();
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
                Load();
		    }
#endif
    }
}