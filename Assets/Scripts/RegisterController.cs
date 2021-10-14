using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;

public class RegisterController : MonoBehaviour
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

        UnityWebRequest request = UnityWebRequest.Post("http://95.216.204.181:8080/api/signin/" + action, formData);

        yield return request.SendWebRequest();

        // �������� ��� ������ � �����
        string recive = request.downloadHandler.text;
        if (recive.Length>0)
        {
            try {
                Debug.Log("����� �����������: "+recive);
                SiginRecive response = JsonUtility.FromJson<SiginRecive>(recive);

                StartCoroutine(LoadMain(response));
            }
            catch
            {
                Error("������ ������ �����������:" + recive);
            }  
        } 
        else 
            Error("������ ����� ����������� "+ request.error);
    }

    public void Error(string error)
    {
        Debug.LogError(error);
        GameObject.Find("error").GetComponent<Text>().text = error;
    }

    private IEnumerator LoadMain(SiginRecive data)
    {
        Debug.Log("�������� ������� �����");

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainScene", new LoadSceneParameters(LoadSceneMode.Additive));
        // asyncLoad.allowSceneActivation = false;

        // Wait until the asynchronous scene fully loads
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        if (data.id == 0 || data.token == null)
            Error("�� ������ player_id ��� token");

        SceneManager.UnloadScene("RegisterScene");
        Camera.main.GetComponent<MainController>().SetPlayer(data);

        // asyncLoad.allowSceneActivation = true;
    }
}
