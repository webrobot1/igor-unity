using Newtonsoft.Json;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MyFantasy
{		
	abstract public class BaseController : MonoBehaviour
	{
		#if UNITY_EDITOR || DEVELOPMENT_BUILD
				// это адрес-мост через наш ПК в wsl сервер Ubuntu (Аналог XAMP и Openserver), подробнее в папке /.docs проекта сервер.
				protected const string SERVER = "127.0.0.1:8080";   //localhost не подходит тк http переадресуются, а websocket пойдут уже на наш ПК
		#else
				protected const string SERVER = "185.117.153.89";   // это физический адрес удаленного vps сервера где крутится prodiction (можно и просто домен указывать) 
		#endif

		// закешированный логин и пароль (может пригодится для повтороного входа в игру)
		protected static string login;
		protected static string password;
		protected static string game_id;

		public static void Error(string error, Exception ex = null)
		{
			GameObject.Find("error").GetComponent<Text>().text = error;
			Debug.LogError(error);

			if (ex != null)
				Debug.LogException(ex);
		}

		protected virtual void Awake()
		{

			// продолжать принимать данные и обновляться в фоновом режиме
			Application.runInBackground = true;

			#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.unityLogger.logEnabled = true;
			#else
				Debug.unityLogger.logEnabled = false;
			#endif

			#if UNITY_WEBGL && !UNITY_EDITOR
				WebGLSupport.WebGLFocus.FocusInit();
			#endif
		}

		protected virtual IEnumerator HttpRequest(string action)
		{
			if (login.Length == 0 || password.Length == 0)
			{
				Error("оттсувует логин или пароль");
				yield break;
			}

			if (game_id.Length == 0)
			{
				Error("разработчик не указал ИД игры сервиса http://my-fantasy.ru/");
				yield break;
			}

			WWWForm formData = new WWWForm();
			formData.AddField("login", login);
			formData.AddField("password", password);
			formData.AddField("game_id", game_id);

			string url = "http://" + SERVER + "/game/signin/" + action;
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
						Error("Ошибка авторизации с сервером "+ SERVER + ": " + recive.error);
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
		protected virtual IEnumerator LoadMain(SigninRecive data)
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
				ConnectController.Connect(data);

				// asyncLoad.allowSceneActivation = true;
			}
		}

		public static void Log(object obj)
		{
			UnityEngine.Debug.Log(System.DateTime.Now.ToLongTimeString() + " : " + obj);
		}
	}
}
