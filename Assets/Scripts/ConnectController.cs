using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Класс для обработки запросов, конект
/// </summary>
public abstract class ConnectController : MonoBehaviour
{
	/// <summary>
	/// true - загружается сцена регистрации (выходим из игры)
	/// </summary>
	private bool exit;

	/// <summary>
	/// Ссылка на конектор
	/// </summary>
	protected Protocol connect;

	/// <summary>
	/// Префаб нашего игрока
	/// </summary>
	protected PlayerModel player;

	/// <summary>
	/// Префаб Sprite Render на котором расположим карту
	/// </summary>
	private GameObject map;

	/// <summary>
	/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте
	/// </summary>
	private int id;

	/// <summary>
	/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
	/// </summary>
	private string token;

	/// <summary>
	/// время от нажатия кнопки идти до ответа сервера (переделать в List)
	/// </summary>
	protected double pingTime;

	/// <summary>
	/// сколько пикселей на 1 Unit должно считаться
	/// </summary>
	private float PixelsPerUnit;

	/// <summary>
	/// время последнего шага нашего игрока
	/// </summary>
	protected DateTime lastMove = DateTime.Now;

	/// <summary>
	/// Позиция к которой движется наш персонаж (пришла от сервера)
	/// </summary>
	protected Vector2 target;

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	private void Update()
	{
		if(connect != null) 
		{ 
			if (connect.error != null)
			{
				StartCoroutine(LoadRegister(connect.error));
			}
			else if (connect.recives != null)
			{
				for (int i = 0; i < connect.recives.Count; i++)
				{
					try
					{
						Debug.Log(DateTime.Now.Millisecond + ": "+ connect.recives[i]);
						HandleData(JsonUtility.FromJson<Recive>(connect.recives[i]));
						connect.recives.RemoveAt(i);
					}
					catch (Exception ex)
					{
						StartCoroutine(LoadRegister(ex.Message + ": " + connect.recives[i]));
						break;
					}
				}
			}
		}
	}


	/// <summary>
	/// Звпускается после авторизации - заполяет id и token 
	/// </summary>
	/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
	public void SetPlayer(SiginRecive data)
	{
		this.id = data.id;
		this.token = data.token;
		this.PixelsPerUnit = data.pixels;
		this.pingTime = Time.fixedDeltaTime = data.time;

		Debug.Log("FixedTime = " + data.time);
		connect = new Websocket();

		GetComponent<Camera>().orthographicSize = GetComponent<Camera>().orthographicSize * 100/data.pixels;

		SigninResponse response = new SigninResponse();
		response.token = data.token;
		response.action = "load";

		connect.Send(response);
	}

	/// <summary>
	/// Обработка пришедших от сервера значений
	/// </summary>
	/// <param name="recive">JSON сигнатура согласно стрктуре ReciveJson</param>
	private void HandleData(Recive recive)
	{
        switch (recive.action)
        {
			case "screen":
				StartCoroutine(Screen());
			return;
        }

		if (recive.error != null)
		{
			StartCoroutine(LoadRegister("Ошибка сервера:" + recive.error));
		}
		else if(!exit) // обновляем мир только если в не выходим из игры (занмиает какое то время)
		{
			// если есть объекты
			if (recive.map.data != null)
			{
				if(map == null)
					map = GameObject.Find("Map");

				map.GetComponent<SpriteRenderer>().sprite = ImageToSpriteModel.Base64ToSprite(recive.map.data, PixelsPerUnit);
			}

			if (recive.players != null)
			{
				foreach (PlayerRecive player in recive.players)
				{
					GameObject prefab = GameObject.Find("player_" + player.id);

					// если игрока нет на сцене
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + player.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "player_" + player.id;
						if (player.id == this.id)
						{
							transform.SetParent(prefab.transform);
							transform.position = new Vector3(transform.parent.position.x, transform.parent.position.y, transform.position.z);
							this.player = prefab.GetComponent<PlayerModel>();
						}
					}

                    try { 
						prefab.GetComponent<PlayerModel>().SetData(player);
					}
					catch (Exception ex)
					{
						StartCoroutine(LoadRegister("Не удалось загрузить игрока:" + ex));
                    }

					// если на сцене и есть position - значит куда то движтся. запишем куда
					if (player.position != null && player.id == this.id)
					{
						this.target = new Vector2(player.position[0], player.position[1]);

						// если мы в движении запишем наш пинг (если только загрузились то оже пишется  - 0)
						// Todo сравнить что координаты изменены (может это не про движения данные пришли)
						if (pingTime == 0)
						{
							TimeSpan ts = DateTime.Now - this.lastMove;
							pingTime = Math.Round(ts.TotalSeconds, 2);
						}
					}
				}
			}

			// если есть враги
			if (recive.enemys != null)
			{
				foreach (EnemyRecive enemy in recive.enemys)
				{
					GameObject prefab = GameObject.Find("enemy_" + enemy.id);
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + enemy.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "enemy_" + enemy.id;
					}
					
					try
					{
						prefab.GetComponent<EnemyModel>().SetData(enemy);
					}
					catch (Exception ex)
					{
						StartCoroutine(LoadRegister("Не удалось загрузить NPC:" + ex));
					}
				}
			}

			// если есть объекты
			if (recive.objects != null)
			{
				foreach (ObjectRecive obj in recive.objects)
				{
					GameObject prefab = GameObject.Find("enemy_" + obj.id);
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + obj.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "object_" + obj.id;
					}

					try
					{
						prefab.GetComponent<ObjectModel>().SetData(obj);
					}
					catch (Exception ex)
					{
						StartCoroutine(LoadRegister("Не удалось загрузить объект:" + ex));
					}
				}
			}
		}
	}

	/// <summary>
	/// снятие скриншета экрана и отправка на сервер
	/// </summary>
	private IEnumerator Screen()
	{
		// We should only read the screen buffer after rendering is complete
		yield return new WaitForEndOfFrame();

		byte[] bytes = ScreenCapture.CaptureScreenshotAsTexture().EncodeToPNG();

		// Create a Web Form
		WWWForm form = new WWWForm();
		form.AddField("token", this.token);
		form.AddBinaryData("screen", bytes);

		UnityWebRequest request = UnityWebRequest.Post("http://95.216.204.181:8080/game/signin/screen", form);

		yield return request.SendWebRequest();

		if (request.result != UnityWebRequest.Result.Success)
		{
			Debug.Log(request.error);
		}
		else
		{
			Debug.Log("Finished Uploading Screenshot");
		}
	}


	/// <summary>
	/// Страница ошибок - загрузка страницы входа
	/// </summary>
	/// <param name="error">сама ошибка</param>
	private IEnumerator LoadRegister(string error)
	{
		Debug.LogError(error);

		connect.error = null;
		connect.recives.Clear();

		if (exit)
		{
			Debug.LogError("уже закрываем игру");
			yield break;
		}
		connect = null;

		if (!SceneManager.GetSceneByName("RegisterScene").IsValid())
		{
			//SceneManager.UnloadScene("MainScene");
			AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("RegisterScene", new LoadSceneParameters(LoadSceneMode.Additive));

			// Wait until the asynchronous scene fully loads
			while (!asyncLoad.isDone)
			{
				yield return null;
			}
		}

		SceneManager.UnloadScene("MainScene");
		Camera.main.GetComponent<RegisterController>().Error(error);
	}

	void OnApplicationQuit()
	{
		connect.Close();
	}
}