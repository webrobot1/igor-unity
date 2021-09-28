using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;

public class RegisterController : MonoBehaviour
{
    public static RegisterController instance;

    public InputField login;
    public InputField password;

    private void Awake()
    {
        if (instance == null)
        {
           instance = this;
        }
        else if (instance != null)
        {
            Debug.LogError("Instance already exists, destroying object!");
            Destroy(this);
        }
    }
    public void Register()
    {
        StartCoroutine(SendRequest("register"));
    }

    public void Sigin()
    {
		StartCoroutine(SendRequest("signin"));     
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

        UnityWebRequest request = UnityWebRequest.Post("http://95.216.204.181:8080/game/signin/" + action, formData);

        yield return request.SendWebRequest();

        // проверим что пришло в ответ
        string recive = request.downloadHandler.text;
        if (recive.Length>0)
        {
            try {
                Debug.Log("Ответ авторизации: "+recive);
                SiginJson response = JsonUtility.FromJson<SiginJson>(recive);
                StartCoroutine(LoadMain(response));
            }
            catch
            {
                Error("Ошибка ответа авторизации:" + recive);
            }  
        } 
        else 
            Error("Пустой ответ авторизации "+ request.error);
    }

    public void Error(string error)
    {
        Debug.LogError(error);
        GameObject.Find("error").GetComponent<Text>().text = error;
    }

    private IEnumerator LoadMain(SiginJson data)
    {
        Debug.Log("Загрузка главной сцены");

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainScene", new LoadSceneParameters(LoadSceneMode.Additive));
        // asyncLoad.allowSceneActivation = false;

        // Wait until the asynchronous scene fully loads
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        if (data.id == 0 || data.token == null)
            Error("не указан player_id или token");

        SceneManager.UnloadScene("RegisterScene");
        Camera.main.GetComponent<MainController>().SetPlayer(data);

        // asyncLoad.allowSceneActivation = true;
    }
}
