using System;
using UnityEngine;

/// <summary>
/// Класс для отправки данных (действий игрока)
/// </summary>
public class MainController : ConnectController
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

    // поиск пути                     	
    //private NavMeshPath path;						
    //private int current_path;

    private void Start()
    {
        // продолжать принимать данные и обновляться в фоновом режиме
        Application.runInBackground = true;

        // наш джойстик
        variableJoystick = GameObject.Find("joystick").GetComponent<VariableJoystick>();
        variableJoystick.SnapX = true;
        variableJoystick.SnapY = true;
    }

    /// <summary>
    /// Проверка на то может ли игрок делать шаг (с учетом его пинга мы разрешаем ему отправлять следующую команду на сервер раньше, чем анимация дойдет до точки). 
    /// Ипользутся интерполяция
    /// Можно и всегда давать нажать кнопку движения (что возможно и будет), но при малом PING игроки с хорошим интернетом будут просто летатть по карте 
    /// Если долго (среднее время пути в анимации) нет ответа то тоже дает идти
    /// </summary>
    /// <returns>true - если может, false - если нет</returns>
    private bool CanMove()
    {
        return

            (DateTime.Compare(this.lastMove.AddMilliseconds(1000), DateTime.Now) < 1) // Todo заменить 1000 на скорость анимации с учетом скорости игрока)
                 ||
            (
                base.pingTime > 0
                    &&
                // разрешаем двигаться далее если осталось пройти растояние что пройдется за время пинга + 1 шаг всегда резервный (на сервере учтено что команду шлем за 1 шаг минимум)
                // учитываем +0.5 к координатам по X которые мы заложили на анимацию движения в центр клетки 
                Vector2.Distance(player.transform.position - new Vector3(0.5f, 0, 0), target) - player.distancePerUpdate <= (base.pingTime< Time.fixedDeltaTime? Time.fixedDeltaTime: base.pingTime) / Time.fixedDeltaTime * player.distancePerUpdate
            );
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (player != null)
        {
            // если ответа  сервера дождались (есть пинг-скорость на движение) и дистанция  такая что уже можно слать новый запрос 
            // или давно ждем (если нас будет постоянно отбрасывать от дистанции мы встанем и сможем идти в другом направлении)
            if (CanMove())
            {
               if ((vertical = Input.GetAxis("Vertical")) != 0 || (vertical = variableJoystick.Vertical) != 0 || (horizontal = Input.GetAxis("Horizontal")) != 0 || (horizontal = variableJoystick.Horizontal) != 0) 
               {
                    MoveResponse response = new MoveResponse(); 

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

                    response.ping = pingTime - Time.fixedDeltaTime;

                    // если мы сделали шаг то нужнотобнулить время пинга
                    pingTime = 0;
                    connect.Send(response);
					
                    // и записать время последнего шага
                    base.lastMove = DateTime.Now;
                }
            }
        }
    }
}