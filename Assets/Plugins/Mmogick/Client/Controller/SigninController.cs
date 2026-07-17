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

			var canvas = GetComponentInParent<Canvas>();
			if (canvas != null) canvas.enabled = false;

			if (serverField != null && serverField.text.Length > 0)
				SERVER = serverField.text;

			WWWForm formData = new WWWForm();
			formData.AddField("slug", login); // поле wire — slug (единая идентичность сущностей на сервере)
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

			if (string.IsNullOrEmpty(data.key))
				Error("Не указан key игрока");

			else if (string.IsNullOrEmpty(data.host))
				Error("Не указан хост сервера");

			else if (string.IsNullOrEmpty(data.token))
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
				// idle_action задаём ДО Connect, чтобы первый же спавн (SpriterPostImportAdjuster Phase 1)
				// мог сразу резолвить idle-клип через ConnectController.idle_action. По контракту сервер
				// ВСЕГДА шлёт непустое поле "idle" в /auth response — пустота = нарушение контракта,
				// падаем громко (CLAUDE.md «не заплатывать»), чтобы баг серверной конфигурации не маскировался.
				if (string.IsNullOrEmpty(data.idle_action))
					throw new System.Exception("Сервер не отдал поле 'idle' в /auth response. По контракту оно обязательно.");

				ConnectController.idle_action = data.idle_action;
				ConnectController.step = data.step;
				ConnectController.position_precision = data.position_precision;
				ConnectController.server_fps = data.fps;

				ConnectController.isDebug = data.isDebug;
				// Финальный гейт клиентских логов — по debug-флагу игры. До /auth флаг неизвестен,
				// потому фазу входа гейтит билд-флаг (BaseController.Awake); здесь переключаем на
				// эффективный isDebug игры (тот же, что включает оверлей коллизий в MapDecodeModel):
				// прод-игра (isDebug=false) логами не спамит.
				Debug.unityLogger.logEnabled = data.isDebug;

				// equipment_slot — справочник slug-ов слотов экипировки игры. По контракту приходит непустой
				// (см. SigninRecive.equipment_slot). UI рисует ровно эти ячейки.
				if (data.equipment_slot == null || data.equipment_slot.Count == 0)
					throw new System.Exception("Сервер не отдал поле 'equipment_slot' в /auth response (или оно пусто). По контракту обязательно.");
				ConnectController.equipment_slot = data.equipment_slot;

				ConnectController.Connect(data.host, data.token, data.key);

				// asyncLoad.allowSceneActivation = true;
			}
		}
	}
}
