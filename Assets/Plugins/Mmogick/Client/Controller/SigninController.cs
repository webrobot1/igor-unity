using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using System;
using System.Text.RegularExpressions;

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

        // Действия публичного API входа: та же строка идёт в адрес запроса и различает ветки ниже.
        private const string ACTION_AUTH = "auth";
        private const string ACTION_REGISTER = "register";

        // Правила логина — те же, что держит сервер, и отбиваются они здесь ради самого игрока: заведомо
        // негодный ввод не стоит ему похода на сервер и ожидания ответа. Истина остаётся серверной, клиент
        // её лишь зеркалит; место у правил одно на обе кнопки формы — обе идут в HttpRequest.
        // Тексты дословно серверные: чинить ввод игрок будет по одной и той же формулировке, какая бы
        // сторона его ни отбила.
        private const int LOGIN_MIN_LENGTH = 3;
        private static readonly Regex LOGIN_ALLOWED = new Regex(@"^[a-z0-9][a-z0-9_\-]*$");
        private static readonly Regex LOGIN_NUMERIC = new Regex(@"^\d+(e\d+)?$");

        // Сколько всего ждём поднятия сервера карты, повторяя вход. Порог человеческий: дольше игрок читает
        // затянувшийся вход как зависание, и честнее показать ошибку, чем крутить попытки дальше.
        private const int MAX_RETRY_SEC = 60;

        // Пауза перед повтором, когда ответ нечитаем и сервер паузы не назвал (сеть, ошибка узла, чужая страница).
        private const int FALLBACK_RETRY_SEC = 3;

        // Предел ожидания ОДНОГО запроса. Без него повисшее соединение (узел принял запрос и молчит, моргнула
        // сеть, оборвалась переадресация портов) не возвращается вовсе, а вся защита выше отсчитывается от
        // ВОЗВРАТА запроса — не срабатывают ни повторы, ни общий предел, и игрок остаётся перед экраном загрузки
        // без ошибки и без конца. Отсечка переводит зависание в обычный нечитаемый ответ, который ветка повторов
        // уже умеет обрабатывать. Держать меньше общего предела: иначе одна повисшая попытка съедает всё окно.
        private const int REQUEST_TIMEOUT_SEC = 10;

        // Момент, после которого повторы прекращаются. Ставится на первой попытке входа (см. HttpRequest).
        private DateTime retryDeadline;

        protected virtual void Start()
        {
            // Error() не бросает — без return метод доводил бы настройку формы на состоянии, которое сам
            // признал нарушенным (тот же порядок, что в CursorController.Awake).
            if (loginField == null)
            {
                Error("Не привязан loginField для ввода логина");
                return;
            }

            if (passwordField == null)
            {
                Error("Не привязан passwordField поле ввода пароля");
                return;
            }

            if (GAME_ID == 0)
            {
                Error("Не заполнен gameIdField для идентификации в одной из игр сервиса http://mmogick.ru/ и зарегистрируйте новую запись");
                return;
            }

            if (serverField != null && SERVER.Length > 0)
                serverField.text = SERVER;

        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;

            StartCoroutine(HttpRequest(ACTION_REGISTER));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;

            StartCoroutine(HttpRequest(ACTION_AUTH));
        }

		private IEnumerator HttpRequest(string action, bool retrying = false)
		{
			if (login.Length == 0 || password.Length == 0)
			{
				Error("Заполните логин или пароль");
				yield break;
			}

			if (login.Length < LOGIN_MIN_LENGTH)
			{
				Error("Логин: не короче " + LOGIN_MIN_LENGTH + " символов");
				yield break;
			}

			if (!LOGIN_ALLOWED.IsMatch(login))
			{
				Error("Логин: строчные латинские буквы, цифры, _ и -");
				yield break;
			}

			if (LOGIN_NUMERIC.IsMatch(login))
			{
				Error("Логин не может быть числом");
				yield break;
			}

			// Предел общего ожидания входа. Ставится на ПЕРВОЙ попытке и переживает повторы: сервер карты может
			// подниматься десятки секунд, но не бесконечно, а без предела цикл повторов не кончился бы никогда.
			if (!retrying)
				retryDeadline = DateTime.Now.AddSeconds(MAX_RETRY_SEC);

			// Экран закрываем с самого нажатия: дальше и до первой отрисовки мира игроку показывать нечего —
			// форма входа гаснет строкой ниже, а сцена меняется на игровую уже посреди ожидания. Ступень
			// держится и на повторах: сервер карты может подниматься десятки секунд, и вход идёт заново.
			LoadingScreen.SetStage(LoadingScreen.Stage.Auth);

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
			request.timeout = REQUEST_TIMEOUT_SEC;

			yield return request.SendWebRequest();

			// Успех и отказ сервер разводит КОДОМ ответа, общих полей у их тел нет: пакет входа разбирается своей
			// структурой, отказ — своей (SigninErrorRecive). Разбор строгий (умолчания в BaseController: пропуск
			// null, отказ на неизвестном поле), и чужое тело в структуре входа роняет его целиком — тогда отказ по
			// существу читался бы нечитаемым ответом, уходил в повторы, а игрок оставался перед экраном загрузки
			// без ошибки и без конца.
			long code = request.responseCode;
			string text = request.downloadHandler != null ? request.downloadHandler.text : "";
			string transport = request.error;

			request.Dispose();

			// Ответа нет либо он не по контракту: моргнула сеть, узел ответил своей ошибкой, пока поднимал карту,
			// вклинился прокси. Для игрока это тот же затянувшийся вход — ждём и пробуем снова.
			string unreadable = null;
			SigninRecive recive = null;
			SigninErrorRecive refusal = null;

			if (text.Length == 0)
				unreadable = "Пустой ответ авторизации с сервера " + SERVER + " (код " + code + "): " + transport;

			else if (code >= 200 && code < 300)
			{
				Debug.Log("Ответ авторизации: " + text);

				try
				{
					recive = JsonConvert.DeserializeObject<SigninRecive>(text);
				}
				catch (Exception ex)
				{
					// Разбор УДАВШЕГОСЯ входа упал — структура клиента разошлась с сервером. Следующий ответ будет
					// тем же, повторы этого не исправят: показываем ошибку и возвращаем игрока к форме входа.
					Error("Ошибка разбора авторизации: (" + text + ") " + ex.Message, ex);
					yield break;
				}
			}

			else
			{
				Debug.Log("Отказ авторизации (код " + code + "): " + text);

				try
				{
					refusal = JsonConvert.DeserializeObject<SigninErrorRecive>(text);
				}
				catch (Exception ex)
				{
					// Тело не по контракту отказа — отвечал не наш сервер (страница прокси, ошибка веб-сервера):
					// разбирать нечего, случай тот же, что и нечитаемый ответ ниже.
					Debug.LogException(ex);
				}

				if (refusal == null || refusal.error.Length == 0)
					unreadable = "Нечитаемый отказ авторизации с сервера " + SERVER + " (код " + code + "): " + text;
			}

			if (unreadable != null)
			{
				if (DateTime.Now < retryDeadline)
				{
					Debug.LogWarning("Авторизация: " + unreadable + ", повтор через " + FALLBACK_RETRY_SEC + " сек.");

					yield return new WaitForSeconds(FALLBACK_RETRY_SEC);
					yield return StartCoroutine(HttpRequest(action, true));
					yield break;
				}

				Error(unreadable);
				yield break;
			}

			if (refusal != null)
			{
				// retry — повторимый отказ (сервер карты ещё поднимается): ждём названное сервером время и заходим
				// снова. Для игрока это затянувшийся вход, а не ошибка, потому Error() тут не зовём — он увёл бы на
				// экран входа с текстом. Отказ по существу (не прошедший проверку ввод, неверный логин, слишком
				// частые входы) сервер повторимым не метит: повтор его не изменит, а игрок ждал бы впустую до
				// общего предела.
				if (refusal.retry > 0 && DateTime.Now < retryDeadline)
				{
					Debug.Log("Авторизация: " + refusal.error + ", повтор через " + refusal.retry + " сек.");

					yield return new WaitForSeconds(refusal.retry);
					yield return StartCoroutine(HttpRequest(action, true));
					yield break;
				}

				// Разбор по полям приходит только у не прошедшего проверку ввода, и показываем игроку именно его:
				// в error сервер кладёт лишь ПЕРВОЕ нарушение, а с ним игрок чинил бы форму по одному полю.
				string message = refusal.error;
				if (refusal.violations != null && refusal.violations.Count > 0)
				{
					message = "";
					foreach (var violation in refusal.violations)
						message += (message.Length > 0 ? "\n" : "") + violation.Key + ": " + violation.Value;
				}

				Error("Ошибка авторизации к серверу " + SERVER + ": " + message);
				yield break;
			}

			// Регистрация данных для входа не отдаёт — в её ответе только ключ игрока, а в мир сервер пускает
			// отдельным входом. Игроку же регистрация обещает игру, потому входим тем же логином и паролем сами:
			// без этого шага загрузка мира начиналась бы без адреса узла и токена.
			if (action == ACTION_REGISTER)
			{
				yield return StartCoroutine(HttpRequest(ACTION_AUTH));
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
				yield return StartCoroutine(TileCacheService.SyncAll(SERVER, GAME_ID, data.token, err => syncError = err,
					part => LoadingScreen.SetStage(LoadingScreen.Stage.Tiles, part)));
				if (syncError != null)
				{
					TileCacheService.ResetCache(GAME_ID);
					Error(syncError);
					yield break;
				}

				// Справочник компонентов игры — свой канал (компонент к анимации отношения не имеет): умолчания
				// значений, состав видов, иконки. До кеша анимаций: цепочка разрешения значения у префаба
				// (AnimationCacheService.GetComponentValue) последним звеном берёт умолчание отсюда.
				// При ошибке чистим свой кеш, как у тайлов и анимаций: справочник едет дельтой, и рассинхрон
				// сам себя вылечит полным ресинком на следующем заходе.
				LoadingScreen.SetStage(LoadingScreen.Stage.Components);
				yield return StartCoroutine(ComponentCacheService.Sync(SERVER, GAME_ID, data.token, err => syncError = err));
				if (syncError != null)
				{
					ComponentCacheService.ResetCache(GAME_ID);
					Error(syncError);
					yield break;
				}

				// Аналогично для анимаций: ZIP картинок (sha256.ext) + per-game library overrides
				yield return StartCoroutine(AnimationCacheService.SyncAll(SERVER, GAME_ID, data.token, err => syncError = err,
					part => LoadingScreen.SetStage(LoadingScreen.Stage.Animations, part)));
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
						LoadingScreen.SetStage(LoadingScreen.Stage.Scene, asyncLoad.progress);
						yield return null;
					}
					SceneManager.UnloadScene("RegisterScene");
				}
				// idle_action задаём ДО Connect, чтобы первый же спавн мог сразу резолвить idle-клип через
				// ConnectController.idle_action. По контракту сервер ВСЕГДА шлёт непустое поле "idle" в
				// /auth response — пустота = нарушение контракта, падаем громко, чтобы баг серверной
				// конфигурации не маскировался.
				if (string.IsNullOrEmpty(data.idle_action))
					throw new Exception("Сервер не отдал поле 'idle' в /auth response. По контракту оно обязательно.");

				ConnectController.idle_action = data.idle_action;
				ConnectController.step = data.step;
				ConnectController.position_precision = data.position_precision;
				ConnectController.server_fps = data.fps;

				// Геометрия упора в преграду: ею клиент повторяет серверный расчёт шага и отличает глухой упор
				// от места, где серверу ещё есть куда шагнуть. По контракту оба поля приходят всегда и строго
				// больше нуля (см. SigninRecive) — иначе клиент считал бы шаг по чужой геометрии молча.
				if (data.creep_depth <= 0)
					throw new Exception("Сервер не отдал поле 'creep_depth' в /auth response либо оно не больше нуля. По контракту оно обязательно.");

				if (data.corner_offset <= 0)
					throw new Exception("Сервер не отдал поле 'corner_offset' в /auth response либо оно не больше нуля. По контракту оно обязательно.");

				if (data.passable_search_radius <= 0)
					throw new Exception("Сервер не отдал поле 'passable_search_radius' в /auth response либо оно не больше нуля. По контракту оно обязательно.");

				ConnectController.creep_depth = data.creep_depth;
				ConnectController.corner_offset = data.corner_offset;
				ConnectController.passable_search_radius = data.passable_search_radius;

				// До Connect: карта приходит следом, и её разбор уже должен знать, что помечать свечением.
				ConnectController.warp_class = data.warp;

				// Мир текущей карты — им обзорная карта отбирает свои карты из общего кеша (см. поля).
				if (data.world == 0)
					throw new Exception("Сервер не отдал поле 'world' в /auth response. По контракту оно обязательно.");

				ConnectController.world = data.world;
				ConnectController.world_name = data.world_name;

				// equipment_slot — справочник slug-ов слотов экипировки игры. По контракту приходит непустой
				// (см. SigninRecive.equipment_slot). UI рисует ровно эти ячейки.
				if (data.equipment_slot == null || data.equipment_slot.Count == 0)
					throw new Exception("Сервер не отдал поле 'equipment_slot' в /auth response (или оно пусто). По контракту обязательно.");
				ConnectController.equipment_slot = data.equipment_slot;

				ConnectController.Connect(data.host, data.token, data.key);

				// asyncLoad.allowSceneActivation = true;
			}
		}
	}
}
