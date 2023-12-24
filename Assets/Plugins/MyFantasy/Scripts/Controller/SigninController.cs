using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System;

namespace MyFantasy
{
    public class SigninController : BaseController
    {
        [SerializeField]
        protected Text loginField;

        [SerializeField]
        protected InputField passwordField;        
        
        [SerializeField]
        protected Text gameIdField;      // ����� ������ ���� ������ id ������ ������� � ������ �������� http://my-fantasy.ru/  ������� ����

        protected virtual void Start()
        {
           if (loginField == null)
               Error("�� �������� loginField ��� ����� ������");

            if (passwordField == null)
                Error("�� �������� passwordField ���� ���� ������");     
            
            if (gameIdField == null)
                Error("�� �������� gameIdField ��� �������������� � ����� �� ���� �������� http://my-fantasy.ru/ � ������������� ����� ������");
        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;
        
            StartCoroutine(HttpRequest("register"));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;

            StartCoroutine(HttpRequest("auth"));     
        }

		private IEnumerator HttpRequest(string action)
		{
			if (login.Length == 0 || password.Length == 0)
			{
				Error("��������� ����� ��� ������");
				yield break;
			}

			if (game_id.Length == 0)
			{
				Error("����������� �� ������ �� ���� ������� http://my-fantasy.ru/");
				yield break;
			}

			WWWForm formData = new WWWForm();
			formData.AddField("login", login);
			formData.AddField("password", password);
			formData.AddField("game_id", game_id);

			string url = "http://" + SERVER + "/game/signin/" + action;
			Debug.Log("����������� � " + url);

			UnityWebRequest request = UnityWebRequest.Post(url, formData);

			yield return request.SendWebRequest();

			// �������� ��� ������ � �����
			string text = request.downloadHandler.text;
			if (text.Length > 0)
			{
				try
				{
					Debug.Log("����� �����������: " + text);
					SigninRecive recive = JsonConvert.DeserializeObject<SigninRecive>(text);

					if (recive.error.Length > 0)
						Error("������ ����������� � �������� " + SERVER + ": " + recive.error);
					else
						StartCoroutine(LoadMain(recive));
				}
				catch (Exception ex)
				{
					Error("������ ������� �����������: (" + text + ")", ex);
				}
			}
			else
				Error("������ ����� ����������� � �������� " + SERVER + ": " + request.error);

			request.Dispose();

			yield break;
		}

		// PS ��� webgl ���������� ��������� profiling � Built Settings ����� �������� ������ �������� ����� ������� �� websocket ������ � ������
		private IEnumerator LoadMain(SigninRecive data)
		{
			Debug.Log("�������� ������� �����");

			if (data.key.Length == 0)
				Error("�� ������ key ������");

			else if (data.host == null)
				Error("�� ������ ���� �������");

			else if (data.token == null)
				Error("�� ������ token");

			else
			{
				if (!SceneManager.GetSceneByName("MainScene").IsValid())
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

				// �� �������� ���� ���������� �� ConnectController ������� ������� �� ������ (� ����-��������� ����� ��� PlayerController)
				ConnectController.Connect(data.host, data.key, data.token, data.step, data.position_precision, data.fps, data.extrapol);

				// asyncLoad.allowSceneActivation = true;
			}
		}
	}
}
