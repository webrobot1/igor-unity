using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEditor;
using Newtonsoft.Json;

public class RegisterController : BaseController
{

    public InputField login;
    public InputField password;

    public void Register()
    {
        StartCoroutine(HttpRequest("register"));
    }

    public void Auth()
    {
		StartCoroutine(HttpRequest("auth"));     
    }

    private IEnumerator HttpRequest(string action)
	{
        if(login.text == "" || password.text == "")
        {
            yield break;
        }

        WWWForm formData = new WWWForm();
        formData.AddField("login", login.text);
        formData.AddField("password", password.text);

        string url = "http://" + SERVER + "/server/signin/" + action;
        Debug.Log("соединяемся с " + url);

        UnityWebRequest request = UnityWebRequest.Post(url, formData);

        yield return request.SendWebRequest();

        // проверим что пришло в ответ
        string text = request.downloadHandler.text;
        if (text.Length>0)
        {
            try {
                Debug.Log("Ответ авторизации: "+ text);
                SiginRecive recive = JsonConvert.DeserializeObject<SiginRecive>(text);

                if (recive.error.Length>0)
                    Error("Ошибка авторизации: " + recive.error);
                else
                    StartCoroutine(LoadMain(recive));
            }
            catch (Exception ex)
            {
                Error("Ошибка разбора авторизации: "+ex.Message+" ("+text+")");
            }  
        } 
        else 
            Error("Пустой ответ авторизации "+request.error);
    }

    public void Error(string error)
    {
        Debug.LogError(error);
        Websocket.errors.Clear();
        GameObject.Find("error").GetComponent<Text>().text = error;
    }

    // PS для webgl необходимо отключить profiling в Built Settings иначе забьется память браузера после прихода по websocket пакета с картой
    private IEnumerator LoadMain(SiginRecive data)
    {
        Debug.Log("Загрузка главной сцены");

        if (data.id == 0)
            Error("не указан player_id");

        else if (data.host == null)
            Error("не указан хост сервера");

        else if (data.token == null)
            Error("не указан token");

        else
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainScene", new LoadSceneParameters(LoadSceneMode.Additive));
            // asyncLoad.allowSceneActivation = false;

            // Wait until the asynchronous scene fully loads
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            SceneManager.UnloadScene("RegisterScene");
            Camera.main.GetComponent<PlayerController>().SetPlayer(data);

            // asyncLoad.allowSceneActivation = true;
        }
    }
}
