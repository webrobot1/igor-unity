using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEditor;
using Newtonsoft.Json;

public class RegisterController : MainController
{
    public InputField login;
    public InputField password;

    public void Register()
    {
        StartCoroutine(SendRequest("register"));
    }

    public void Auth()
    {
		StartCoroutine(SendRequest("auth"));     
    }

    private IEnumerator SendRequest(string action)
	{

        if(login.text == "" || password.text == "")
        {
            yield break;
        }

        WWWForm formData = new WWWForm();
        formData.AddField("login", login.text);
        formData.AddField("password", password.text);

        UnityWebRequest request = UnityWebRequest.Post("http://"+SERVER+"/server/signin/" + action, formData);

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

        if (data.port == 0)
            Error("не указан port");

        if (data.token == null)
            Error("не указан token");

        if (data.map == null)
            Error("не указан map");

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
