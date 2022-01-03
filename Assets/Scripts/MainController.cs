using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Класс для отправки данных (действий игрока)
/// </summary>
public class MainController : ConnectController
{


    private AndroidJavaClass unityClass;
    private AndroidJavaObject unityActivity;
    private AndroidJavaClass customClass;

    private const string PlayerPrefsTotalSteps = "totalSteps";
    private const string PackageName = "com.kdg.toast.plugin.Bridge";
    private const string UnityDefaultJavaClassName = "com.unity3d.player.UnityPlayer";
    private const string CustomClassReceiveActivityInstanceMethod = "ReceiveActivityInstance";

    private const string CustomClassStartServiceMethod = "StartService";
    private const string CustomClassStopServiceMethod = "StopService";
    private const string CustomClassGetCurrentStepsMethod = "GetCurrentSteps";
    private const string CustomClassSyncDataMethod = "SyncData";

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

    void SendActivityReference()
    {
        Debug.Log("sdf1");
        unityClass = new AndroidJavaClass(UnityDefaultJavaClassName);
        unityActivity = unityClass.GetStatic<AndroidJavaObject>("currentActivity");
        Debug.Log(unityActivity.Call<AndroidJavaObject>("getClass").Call<string>("getCanonicalName"));

        customClass = new AndroidJavaClass(PackageName);
        customClass.CallStatic(CustomClassReceiveActivityInstanceMethod, unityActivity);
        Debug.Log("sdf2");
    }


    public void StartService()
    {
        customClass.CallStatic(CustomClassStartServiceMethod);
        GetCurrentSteps();
    }

    public void StopService()
    {
        customClass.CallStatic(CustomClassStopServiceMethod);
    }

    public void GetCurrentSteps()
    {
        int? stepsCount = customClass.CallStatic<int>(CustomClassGetCurrentStepsMethod);
        Debug.Log(stepsCount.ToString());
    }

    public void SyncData()
    {
        var data = customClass.CallStatic<string>(CustomClassSyncDataMethod);

        var parsedData = data.Split('#');
        var dateOfSync = parsedData[0] + " - " + parsedData[1];
        Debug.Log(dateOfSync);
        var receivedSteps = int.Parse(parsedData[2]);
        var prefsSteps = PlayerPrefs.GetInt(PlayerPrefsTotalSteps, 0);
        var prefsStepsToSave = prefsSteps + receivedSteps;
        PlayerPrefs.SetInt(PlayerPrefsTotalSteps, prefsStepsToSave);
        Debug.Log(prefsStepsToSave.ToString());

        GetCurrentSteps();
    }


    private void Start()
    {
        // продолжать принимать данные и обновляться в фоновом режиме
        Application.runInBackground = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        SendActivityReference();
        StartService();
#endif


        // наш джойстик
        variableJoystick = GameObject.Find("joystick").GetComponent<VariableJoystick>();
        variableJoystick.SnapX = true;
        variableJoystick.SnapY = true;

        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = new Vector3(0, 1f, -1f);
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
        // Todo заменить 1000 на скорость анимации с учетом скорости игрока)
        if (DateTime.Compare(this.lastMove.AddMilliseconds(1000), DateTime.Now) < 1)
        {
            Debug.Log("Слишком долго ждали движения");
            return true;
        }

        // разрешаем двигаться далее если осталось пройти растояние что пройдется за время пинга + 1 шаг всегда резервный (на сервере учтено что команду шлем за 1 шаг минимум)
        if (base.pingTime > 0 && Vector2.Distance(player.transform.position, target) - player.distancePerUpdate <= (base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) / Time.fixedDeltaTime * player.distancePerUpdate)
        {
            Debug.Log("осталось пройти " + (Vector2.Distance(player.transform.position, target) - player.distancePerUpdate) + " клетки и это меньше чем мы успеваем пройти за пинг " + ((base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) + " , те "+ (base.pingTime < Time.fixedDeltaTime ? Time.fixedDeltaTime : base.pingTime) / Time.fixedDeltaTime * player.distancePerUpdate)+" клетки");

            return true;
        }

        return false;
    }

    public void OnApplicationFocus(bool focus)
    {
        Debug.LogError("фокус");
    }

    public void OnApplicationPause(bool pause)
    {
    
    }


    // Update is called once per frame
    private void Update()
   {
        base.Update();

        if (player != null)
        {
            // по клику мыши отправим серверу начать расчет пути к точки и двигаться к ней
            if (Input.GetMouseButtonDown(0))
            {
                player.moveTo = Vector2Int.RoundToInt(GetComponent<Camera>().ScreenToWorldPoint(Input.mousePosition));
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
                player.moveTo != Vector2.zero
            ) 
            {
                if (CanMove())
                {
                    MoveResponse response = new MoveResponse();

                    if (vertical != 0 || horizontal != 0)
                    {
                        player.moveTo = Vector2.zero;

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
                    else if (Vector2.Distance(player.transform.position, player.moveTo) >= 1)
                    {
                        response.action = "move/to";
                        response.to = player.moveTo.x.ToString() + ',' + player.moveTo.y.ToString();
                    }
                    else
                    {
                        player.moveTo = Vector2.zero;
                        return;
                    }

                    response.ping = Math.Round(pingTime - Time.fixedDeltaTime, 4);

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