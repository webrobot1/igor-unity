using System;
using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{		
	abstract public class BaseController : MonoBehaviour
	{
		protected const string SERVER = "127.0.0.1:8080";   //localhost не подходит тк http переадресуются, а websocket пойдут уже на наш ПК

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

		public static void Log(object obj)
		{
			UnityEngine.Debug.Log(System.DateTime.Now.ToLongTimeString() + " : " + obj);
		}
	}
}
