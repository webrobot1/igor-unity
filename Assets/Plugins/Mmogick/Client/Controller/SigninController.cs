using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System;

namespace Mmogick
{
    public class SigninController : BaseController
    {
        [SerializeField]
        protected Text loginField;

        [SerializeField]
        protected InputField passwordField;        
        
        protected virtual void Start()
        {
           if (loginField == null)
               Error("не присвоен loginField для ввода логина");

            if (passwordField == null)
                Error("не присвоен passwordField дляв вода пароля");     
            
            if (GAME_ID == 0)
                Error("не присвоен gameIdField для индентификации в какую ИД игры сервеиса http://my-fantasy.ru/ у разработкчика нужно играть");
        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
        
            StartCoroutine(HttpRequest("register"));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;

            StartCoroutine(HttpRequest("auth"));     
        }

		private IEnumerator HttpRequest(string action)
		{
			if (login.Length == 0 || password.Length == 0)
			{
				Error("оттсувует логин или пароль");
				yield break;
			}

			WWWForm formData = new WWWForm();
			formData.AddField("login", login);
			formData.AddField("password", password);

			string url = "http://" + SERVER + "/game/signin/" + action+"/?game_id="+GAME_ID;
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
						Error("Ошибка авторизации с сервером " + SERVER + ": " + recive.error);
					else
						StartCoroutine(LoadMain(recive));
				}
				catch (Exception ex)
				{
					Error("Ошибка разбора авторизации: (" + text + ")", ex);
				}
			}
			else
				Error("Пустой ответ авторизации с сервером " + SERVER + ": " + request.error);

			request.Dispose();

			yield break;
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

				// он вывзовет того наследника от ConnectController который повешан на камеру (в игре-песочнице Игорь это PlayerController)
				ConnectController.Connect(data.host, data.key, data.token, data.step, data.position_precision, data.fps);

				// asyncLoad.allowSceneActivation = true;
			}
		}
	}
}
