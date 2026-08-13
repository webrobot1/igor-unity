using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	abstract public class BaseController : MonoBehaviour
	{
		// Пришедшее с сервера поле, которого нет в структуре клиента, роняет разбор пакета целиком — расхождение
		// вскрывается сразу, а не молчаливой потерей данных. Гейт по типу сборки, а не по настройке игрока
		// «Тестовый режим»: та приезжает отдельным пакетом уже ПОСЛЕ входа (см. Awake ниже) и первый — самый
		// полный — пакет не покрыла бы, а падение разбора у играющего значит неработающий клиент, чинимый только
		// правкой структур и пересборкой. Ставится до загрузки сцены: JsonConvert.DefaultSettings действует и там,
		// где вызов передаёт свои JsonSerializerSettings (CreateDefault мержит их поверх умолчаний).
		#if UNITY_EDITOR || DEVELOPMENT_BUILD
			[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
			static void InitJsonSettings()
			{
				JsonConvert.DefaultSettings = () => new JsonSerializerSettings
				{
					MissingMemberHandling = MissingMemberHandling.Error
				};
			}
		#endif

		// Настройки соединения с сервером
		public static int GAME_ID = 1;                     // здесь должен быть указан id ВАШЕГО проекта в личном кабинете http://mmogick.ru  раздела Игры
		protected string SERVER = "localhost";			   // это физический адрес удаленного vps сервера где крутится prodiction (дефолтное значение, можно переопределить через UI)

		// закешированный логин и пароль (может пригодится для повтороного входа в игру)
		protected static string login;
		protected static string password;

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

		public static void Log(object obj)
		{
			UnityEngine.Debug.Log(System.DateTime.Now.ToLongTimeString() + " : " + obj);
		}
	}
}
