using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Класс для отправки данных (действий игрока)
/// </summary>
public class GameController : ConnectController
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

    private Vector2 moveTo = Vector2.zero;

    private void Start()
    {
        // продолжать принимать данные и обновляться в фоновом режиме
        Application.runInBackground = true;

        // наш джойстик
        variableJoystick = GameObject.Find("joystick").GetComponent<VariableJoystick>();
        variableJoystick.SnapX = true;
        variableJoystick.SnapY = true;

        #if UNITY_EDITOR
                Debug.unityLogger.logEnabled = false;
        #else
                Debug.unityLogger.logEnabled = false;
        #endif
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
        // если сделали шаг и давно жде ответа
        // Todo заменить 1000 на скорость анимации с учетом скорости игрока)
        if (base.pingTime == 0 && DateTime.Compare(base.lastMove.AddMilliseconds(1000), DateTime.Now) < 1)
        {
            Debug.LogWarning("Слишком долго ждали движения");
            return true;
        }

        // разрешаем двигаться далее если осталось пройти растояние что пройдется за время пинга + 1 шаг всегда резервный (на сервере учтено что команду шлем за 1 шаг минимум)
        if (base.pingTime > 0 && Vector2.Distance(player.transform.position, target) - player.distancePerUpdate <= (base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) / Time.fixedDeltaTime * player.distancePerUpdate)
        {
            Debug.Log("осталось пройти " + (Vector2.Distance(player.transform.position, target) - player.distancePerUpdate) + " клетки и это меньше чем мы успеваем пройти за пинг " + ((base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) + " сек. , те "+ (base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) / Time.fixedDeltaTime * player.distancePerUpdate)+" клетки");

            return true;
        }

        return false;
    }


    // Update is called once per frame
    private void Update()
   {
        base.Update();

        if (player != null && !pause && !exit)
        {
            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                moveTo = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
                Debug.Log(moveTo);
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
                moveTo != Vector2.zero
            ) 
            {
                if (CanMove()) 
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
                    else if (Vector2.Distance(player.transform.position, moveTo) >= 1)
                    {
                        response.action = "move/to";
                        response.to = moveTo.x.ToString() + ',' + moveTo.y.ToString();

                        // очищаем переменную после того как команда отправлена (дальше сам пойдет)
                        moveTo = Vector2.zero;
                    }
                    else
                    {
                        return;
                    }

                    response.ping = Math.Round(base.pingTime - Time.fixedDeltaTime, 3);

                    // если мы сделали шаг то нужнотобнулить время пинга
                    base.pingTime = 0;
                    connect.Send(response);
					
                    // и записать время последнего шага
                    base.lastMove = DateTime.Now;
                }
            }
        }
    }
}