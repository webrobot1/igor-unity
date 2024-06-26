using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{		
	abstract public class BaseController : MonoBehaviour
	{
		[Header("Настройки соединения с сервером")]
		protected int GAME_ID = 2;                         // здесь должен быть указан id ВАШЕГО проекта в личном кабинете http://mmogick.ru  раздела Игры
		protected string SERVER = "mmogick.ru";			   // это физический адрес удаленного vps сервера где крутится prodiction (можно и просто домен указывать) 

		// закешированный логин и пароль (может пригодится для повтороного входа в игру)
		protected static string login;
		protected static string password;

		public static void  Error(string error = null, Exception ex = null)
		{
            if (error != null) 
			{ 
				GameObject.Find("error").GetComponent<Text>().text = error;
				Debug.LogError(error);
			}

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

		public static void Log(object obj)
		{
			UnityEngine.Debug.Log(System.DateTime.Now.ToLongTimeString() + " : " + obj);
		}
	}
}
