using Newtonsoft.Json;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

abstract public class BaseController : MonoBehaviour
{
    // protected const string SERVER = "185.117.153.89";
    protected const string SERVER = "my-fantasy";                   // сервер авторизации и карт. тк у меня wsl и WWWForm (http из под C#) надо ставить что то отличное от localhost


    // закешированный логин и пароль (может пригодится для повтороного входа в игру)
    protected static string login;
    protected static string password;

#if UNITY_WEBGL && !UNITY_EDITOR
        // заплатка для потери фокуса в webgl (если фокус падает на канву с игрой то обратно на странице html не доступны для ввода поля)
         public void Focus(int focus)
         {
            Debug.Log("Фокус: "+focus);

            if (focus == 0) 
            {
                Input.ResetInputAxes();
                UnityEngine.WebGLInput.captureAllKeyboardInput = false;
            } else
            {
                WebGLInput.captureAllKeyboardInput = true;
                Input.ResetInputAxes();
            }
        }        
#endif

    public abstract void Error(string text);

    protected IEnumerator HttpRequest(string action)
    {
        if (login.Length == 0 || password.Length == 0)
        {
            Error("оттсувует логин или пароль");
            yield break;
        }

        WWWForm formData = new WWWForm();
        formData.AddField("login", login);
        formData.AddField("password", password);

        string url = "http://" + SERVER + "/server/signin/" + action;
        Debug.Log("соединяемся с " + url);

        UnityWebRequest request = UnityWebRequest.Post(url, formData);

        yield return request.SendWebRequest();

        // проверим что пришло в ответ
        string text = request.downloadHandler.text;
        if (text.Length > 0)
        {
            try
            {
                Debug.Log("Ответ авторизации: " + text);
                SigninRecive recive = JsonConvert.DeserializeObject<SigninRecive>(text);

                if (recive.error.Length > 0)
                    Error("Ошибка авторизации: " + recive.error);
                else
                    StartCoroutine(LoadMain(recive));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Error("Ошибка разбора авторизации: (" + text + ")");
            }
        }
        else
            Error("Пустой ответ авторизации " + request.error);
    }

    // PS для webgl необходимо отключить profiling в Built Settings иначе забьется память браузера после прихода по websocket пакета с картой
    private IEnumerator LoadMain(SigninRecive data)
    {
        Debug.Log("Загрузка главной сцены");

        if (data.key.Length == 0)
            Error("не указан key игрока");

        else if (data.host == null)
            Error("не указан хост сервера");

        else if (data.token == null)
            Error("не указан token");

        else
        {
            if (SceneManager.GetActiveScene().name != "MainScene")
            {
                AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainScene", new LoadSceneParameters(LoadSceneMode.Additive));
                // asyncLoad.allowSceneActivation = false;

                // Wait until the asynchronous scene fully loads
                while (!asyncLoad.isDone)
                {
                    yield return null;
                }

                SceneManager.UnloadScene("RegisterScene");
            }

            Camera.main.GetComponent<PlayerController>().SetPlayer(data);

            // asyncLoad.allowSceneActivation = true;
        }
    }
}
