using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Mmogick
{
	abstract public class BaseController : MonoBehaviour
	{
		// Умолчания разбора и сериализации JSON. Ставятся до загрузки сцены: JsonConvert.DefaultSettings действует
		// и там, где вызов передаёт свои JsonSerializerSettings (CreateDefault мержит их поверх умолчаний).
		//
		// NullValueHandling.Ignore нужен в сборке любого типа: сервер шлёт скаляры всегда, включая null (канон его
		// сериализации: null ≡ дефолт поля), а без Ignore null пишется в не-nullable поле C# и роняет разбор.
		//
		// MissingMemberHandling.Error гейтится ТИПОМ СБОРКИ: пришедшее с сервера поле, которого нет в структуре
		// клиента, роняет разбор пакета целиком — расхождение вскрывается сразу, а не молчаливой потерей данных;
		// у играющего же падение разбора значит неработающий клиент, чинимый только правкой структур и пересборкой,
		// потому в релизной сборке проверки нет. Настройка игрока «Тестовый режим» гейтом тут не годится: она
		// приезжает отдельным пакетом уже ПОСЛЕ входа (см. Awake ниже) и первый — самый полный — пакет не покрыла бы.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void InitJsonSettings()
		{
			JsonConvert.DefaultSettings = () => new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore,
				#if UNITY_EDITOR || DEVELOPMENT_BUILD
					MissingMemberHandling = MissingMemberHandling.Error,
				#endif
			};
		}

		// Имена сцен клиента: их называют обе стороны перехода — вход грузит игровую и выгружает сцену
		// входа, возврат делает обратное, — и литералами они разъехались бы молча.
		public const string SCENE_REGISTER = "RegisterScene";
		public const string SCENE_MAIN = "MainScene";

		// Настройки соединения с сервером
		protected string SERVER = "localhost";			   // это физический адрес удаленного vps сервера где крутится prodiction (дефолтное значение, можно переопределить через UI)

		// закешированный логин и пароль (может пригодится для повтороного входа в игру)
		protected static string login;
		protected static string password;

		/// <summary>
		/// Включить либо погасить у сцены то, что движок держит одним экземпляром на показанное игроку: ввод
		/// интерфейса (EventSystem) и слушателя звука (AudioListener). Они у каждой сцены клиента свои, а на
		/// переходе между сценами обе загружены разом: второй включённый движок встречает сообщением в журнале
		/// и повторяет его, пока включены оба. Потому уходящая сцена гасит своё ДО загрузки сменяющей. Загруженная
		/// сцена приходит со своими включёнными, а сцена, оставшаяся загруженной под прерванным переходом, включает
		/// их обратно, когда снова показана игроку.
		/// </summary>
		protected static void SetSceneFocus(Scene scene, bool focused)
		{
			Toggle<EventSystem>();
			Toggle<AudioListener>();

			void Toggle<T>() where T : Behaviour
			{
				foreach (T behaviour in FindObjectsByType<T>(FindObjectsInactive.Exclude))
					if (behaviour.gameObject.scene == scene)
						behaviour.enabled = focused;
			}
		}

		public static void  Error(string error = null, Exception ex = null)
		{
            if (error != null)
			{
				// Текст читает игрок на сцене входа — панель загрузки, если она поднята, экран освобождает.
				LoadingScreen.Hide();

				var errorObj = GameObject.Find("error");
				var canvas = errorObj.GetComponentInParent<Canvas>();
				if (canvas != null) canvas.enabled = true;
				errorObj.GetComponent<Text>().text = error;
				Debug.LogError(error);
			}

			if (ex != null)
				Debug.LogException(ex);
		}

		protected virtual void Awake()
		{
			// продолжать принимать данные и обновляться в фоновом режиме
			Application.runInBackground = true;

			// Вход и загрузку карты логируем в любом билде: настройка игрока «Тестовый режим», которой логи
			// гейтятся дальше, приезжает отдельным пакетом уже ПОСЛЕ входа, а сорвавшийся вход разбирать
			// нечем — молчащий клиент следа не оставляет. Дальнейшее включение и гашение — в
			// SettingsController.SetTestMode по этой настройке.
			Debug.unityLogger.logEnabled = true;

			#if UNITY_WEBGL && !UNITY_EDITOR
				WebGLSupport.WebGLFocus.FocusInit();
			#endif
		}

		// Своей метки времени тут нет: её ставит единая точка журнала (Mmogick.Debug), и второй формат
		// рядом разводил бы соседние строки одного журнала — «21:11:28 : …» против «[21:11:28:050] …».
		public static void Log(object obj)
		{
			Debug.Log(obj);
		}
	}
}
