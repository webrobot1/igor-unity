using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyFantasy
{
    public abstract class CursorController : ActionBarsController
    {
        /// <summary>
        /// нажата кнопка двигаться по горизонтали
        /// </summary>
        private float horizontal;

        /// <summary>
        /// нажата кнопка двигаться по вертикали
        /// </summary>
        private float vertical;

        private Vector3 move_to = Vector3.zero;

        [Header("Для работы с курсором и движением")]

        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        protected VariableJoystick joystick;

        /// <summary>
        /// Объект с компонентом Image
        /// </summary>
        [SerializeField]
        private Image cursor;

        /// <summary>
        /// An offset to move the icon away from the mouse
        /// </summary>
        [SerializeField]
        private Vector3 cursor_offset;

        /// <summary>
        /// если не null - то объект который двигаем
        /// </summary>
        public static MoveableObject MyMoveable;
        protected override void Awake()
        {
            base.Awake();

            if (cursor == null)
            {
                Error("не присвоен GameObject курсора с image компонентом");
                return;
            }
              
            if (joystick == null)
            {
                Error("не указан джойстик");
                return;
            }   
        }

        /// <summary>
        /// если мы стреляем и продолжаем идти заблокируем поворот (он без запроса к серверу делется) в сторону хотьбы (а то спиной стреляем)
        /// </summary>
        private DateTime block_forward = DateTime.Now;

        protected override void Update ()
        {
            base.Update();

            //Makes sure that the icon follows the hand
            cursor.transform.position = Input.mousePosition + cursor_offset;

            if (MyMoveable!=null)
                cursor.raycastTarget = true;
            else
                cursor.raycastTarget = false;

            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                cursor.raycastTarget = false;
                GameObject gameObject = null;

                RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero, Mathf.Infinity);

                // кликнули на какой то объект
                if (hit.transform != null && hit.transform.gameObject != null && hit.transform.gameObject.GetComponent<EntityModel>())
                {
                    gameObject = hit.transform.gameObject;
                    player.Log("Кликнули на объект " + gameObject.name);
                }

                else if
                (
                    (EventSystem.current.IsPointerOverGameObject() || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)))
                )
                {
                    PointerEventData pointerData = new PointerEventData(EventSystem.current);
                    pointerData.position = Input.mousePosition;

                    List<RaycastResult> results = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(pointerData, results);

                    if (results.Count > 0)
                    {
                        gameObject = results[0].gameObject;
                        player.Log("Кликнули на UI " + gameObject.name);
                    }    
                }


                if (MyMoveable != null)
                {
                    Vector2 pos = (Camera.main.ScreenToWorldPoint(Input.mousePosition) - PlayerController.Player.transform.position).normalized;

                    if (player != null && PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.Player.hp > 0)
                    {
                        PlayerController.Player.Forward = new Vector3(pos.x, pos.y, PlayerController.Player.Forward.z);
                        MyMoveable.Use(gameObject);
                    }

                    MyMoveable = null; 
                    CloseAllMenu();
                    cursor.color = new Color(0, 0, 0, 0);
                }
                else
                {
                    if(gameObject == null)
                    {
                        Target = null;
                        persist_target = false;
                        Debug.Log("Кликнули на " + Camera.main.ScreenToWorldPoint(Input.mousePosition));

                        // движение к указанной клетке
                        if (player != null)
                        {
                            move_to = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                            if (Vector3.Distance(player.position, move_to) < 1.15f)
                                move_to = Vector3.zero;
                        }
                    }
                    else
                    {
                        ObjectModel new_target = gameObject.GetComponent<ObjectModel>();
                        if (new_target != null)
                        {
                            Target = new_target;
                            persist_target = true;
                        }
                    }
                }
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
                                if (DateTime.Compare(block_forward, DateTime.Now) < 1)
                                   player.Forward = vector;

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

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            // если с сервера пришла анимация заблокируем повороты вокруг себя на какое то время (а то спиной стреляем идя и стреляя)
            if (player != null && key == player.key && recive.action != null)
            {
                block_forward = DateTime.Now.AddSeconds(0.2f);
            }

            return base.UpdateObject(map_id, key, recive, type);
        }
        /// <summary>
        /// Метод вызываемый при перетаскивании
        /// </summary>
        /// <param name="moveable">The moveable to pick up</param>
        public static void TakeMoveable(MoveableObject moveable)
        {
            MyMoveable = moveable;
            MainController.Instance.cursor.sprite = moveable.Image.sprite;
            MainController.Instance.cursor.color = Color.white;
        }
    }
}
