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

        [SerializeField]
        protected InputField serverField;

        protected virtual void Start()
        {
           if (loginField == null)
               Error("Не привязан loginField для ввода логина");

            if (passwordField == null)
                Error("Не привязан passwordField поле ввода пароля");

            if (GAME_ID == 0)
                Error("Не заполнен gameIdField для идентификации в одной из игр сервиса http://mmogick.ru/ и зарегистрируйте новую запись");

            if (serverField != null && SERVER.Length > 0)
                serverField.text = SERVER;
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
				Error("Заполните логин или пароль");
				yield break;
			}

			if (serverField != null && serverField.text.Length > 0)
				SERVER = serverField.text;

			WWWForm formData = new WWWForm();
			formData.AddField("login", login);
			formData.AddField("password", password);

			string url = "http://" + SERVER + "/api/game/" + GAME_ID + "/" + action;
			Debug.Log("Подключение к " + url);

			UnityWebRequest request = UnityWebRequest.Post(url, formData);

			yield return request.SendWebRequest();

			// проверка что данные в ответ
			string text = request.downloadHandler.text;
			if (text.Length > 0)
			{
				try
				{
					Debug.Log("Ответ авторизации: " + text);
					SigninRecive recive = JsonConvert.DeserializeObject<SigninRecive>(text);

					if (recive.error.Length > 0)
						Error("Ошибка авторизации к серверу " + SERVER + ": " + recive.error);
					else
						StartCoroutine(LoadMain(recive));
				}
				catch (Exception ex)
				{
					Error("Ошибка разбора авторизации: (" + text + ")", ex);
				}
			}
			else
				Error("Пустой ответ авторизации с сервера " + SERVER + ": " + request.error);

			request.Dispose();

			yield break;
		}

		// PS для webgl рекомендую отключить profiling в Build Settings чтобы заполнит память браузера после прихода по websocket пакетов в логах
		private IEnumerator LoadMain(SigninRecive data)
		{
			Debug.Log("Загрузка игровой сцены");

			if (data.key.Length == 0)
				Error("Не указан key игрока");

			else if (data.host == null)
				Error("Не указан хост сервера");

			else if (data.token == null)
				Error("Не указан token");

			else
			{
			
				// Content-addressable кеш тайлов: архив графики + мета (If-Modified-Since) ДО входа в игру.
				// При ошибке — чистим локальный кеш: рассинхрон с сервером самовосстанавливается при следующем заходе.
				string syncError = null;
				yield return StartCoroutine(TileCacheService.SyncAll(SERVER, GAME_ID, data.token, err => syncError = err));
				if (syncError != null)
				{
					TileCacheService.ResetCache(GAME_ID);
					Error(syncError);
					yield break;
				}

				// Аналогично для анимаций: ZIP картинок (sha256.ext) + per-game library overrides
				yield return StartCoroutine(AnimationCacheService.SyncAll(SERVER, GAME_ID, data.token, err => syncError = err));
				if (syncError != null)
				{
					AnimationCacheService.ResetCache(GAME_ID);
					Error(syncError);
					yield break;
				}
				
				if (!SceneManager.GetSceneByName("MainScene").IsValid())
				{
					Debug.Log("Загружаю сцену игры ");
					AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("MainScene", new LoadSceneParameters(LoadSceneMode.Additive));
					// asyncLoad.allowSceneActivation = false;

					// Wait until the asynchronous scene fully loads
					while (!asyncLoad.isDone)
					{
						yield return null;
					}
					SceneManager.UnloadScene("RegisterScene");
				}
				// не забывайте этот контроллер на ConnectController который создали на сцене (в папе-контейнере может быть PlayerController)
				ConnectController.Connect(data.host, data.key, data.token, data.step, data.position_precision, data.fps);

				// asyncLoad.allowSceneActivation = true;
			}
		}
	}
}
