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

        // Сколько всего ждём поднятия сервера карты, повторяя вход. Порог человеческий: дольше игрок читает
        // затянувшийся вход как зависание, и честнее показать ошибку, чем крутить попытки дальше.
        private const int MAX_RETRY_SEC = 60;

        // Пауза перед повтором, когда ответ нечитаем и сервер паузы не назвал (сеть, ошибка узла, чужая страница).
        private const int FALLBACK_RETRY_SEC = 3;

        // Момент, после которого повторы прекращаются. Ставится на первой попытке входа (см. HttpRequest).
        private DateTime retryDeadline;

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

		private IEnumerator HttpRequest(string action, bool retrying = false)
		{
			if (login.Length == 0 || password.Length == 0)
			{
				Error("Заполните логин или пароль");
				yield break;
			}

			// Предел общего ожидания входа. Ставится на ПЕРВОЙ попытке и переживает повторы: сервер карты может
			// подниматься десятки секунд, но не бесконечно, а без предела цикл повторов не кончился бы никогда.
			if (!retrying)
				retryDeadline = DateTime.Now.AddSeconds(MAX_RETRY_SEC);

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
			string text = request.downloadHandler != null ? request.downloadHandler.text : "";
			string failure = null;
			SigninRecive recive = null;

			if (text.Length > 0)
			{
				try
				{
					Debug.Log("Ответ авторизации: " + text);
					// NullValueHandling.Ignore обязателен для любого серверного payload: сервер шлёт скаляры всегда,
					// включая null (null ≡ дефолт поля), а без Ignore Newtonsoft пишет null в не-nullable поле и падает.
					recive = JsonConvert.DeserializeObject<SigninRecive>(text,
						new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
				}
				catch (Exception ex)
				{
					failure = "Ошибка разбора авторизации: (" + text + ") " + ex.Message;
				}
			}
			else
				failure = "Пустой ответ авторизации с сервера " + SERVER + " (код " + request.responseCode + "): " + request.error;

			request.Dispose();

			if (recive == null)
			{
				// Ответа нет либо он нечитаем: моргнула сеть, узел ответил своей ошибкой, пока поднимал карту, вклинился
				// прокси. Для игрока это тот же затянувшийся вход, что и штатное «поднимается», и обрывать им уже
				// идущее ожидание нельзя — ждём и пробуем снова, пока не вышел общий предел.
				if (DateTime.Now < retryDeadline)
				{
					Debug.LogWarning("Авторизация: " + failure + ", повтор через " + FALLBACK_RETRY_SEC + " сек.");

					yield return new WaitForSeconds(FALLBACK_RETRY_SEC);
					yield return StartCoroutine(HttpRequest(action, true));
					yield break;
				}

				Error(failure);
				yield break;
			}

			if (recive.error.Length > 0)
			{
				// retry — повторимый отказ (сервер карты ещё поднимается): ждём названное сервером время и заходим
				// снова. Для игрока это затянувшийся вход, а не ошибка, потому Error() тут не зовём — он увёл бы на
				// экран входа с текстом.
				if (recive.retry > 0 && DateTime.Now < retryDeadline)
				{
					Debug.Log("Авторизация: " + recive.error + ", повтор через " + recive.retry + " сек.");

					yield return new WaitForSeconds(recive.retry);
					yield return StartCoroutine(HttpRequest(action, true));
					yield break;
				}

				Error("Ошибка авторизации к серверу " + SERVER + ": " + recive.error);
				yield break;
			}

			StartCoroutine(LoadMain(recive));
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
