using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Соединение с сервером, заполнение данными переменных, обработка запросов
/// </summary>
public abstract class ConnectController : MonoBehaviour
{
	/// <summary>
	/// не null если идет загрузка сцены
	/// </summary>
	private bool loading;	
	
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
	public GameObject map;

	/// <summary>
	/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте
	/// </summary>
	private int id;

	/// <summary>
	/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
	/// </summary>
	private string token;

	/// <summary>
	/// действие нашего игрока
	/// </summary>
	[SerializeField]
	protected string action;

	/// <summary>
	/// время от нажатия кнопки идти до ответа сервера
	/// </summary>
	[SerializeField]
	protected double pingTime = 0.2;

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
		if (Protocol.error != null)
		{
			StartCoroutine(LoadRegister(Protocol.error));
		}
		else if (Protocol.recives != null)
		{
			for (int i = 0; i < Protocol.recives.Count; i++)
			{
				try
				{
					Debug.Log(DateTime.Now.Millisecond + ": "+Protocol.recives[i]);
					HandleData(JsonUtility.FromJson<ReciveJson>(Protocol.recives[i]));
				}
				catch (Exception ex)
				{
					StartCoroutine(LoadRegister(ex.Message + ": " + Protocol.recives[i]));
					break;
				}
				
				Protocol.recives.RemoveAt(i);
			}
		}
	}


	/// <summary>
	/// Звпускается после авторизации - заполяет id и token 
	/// </summary>
	/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
	public void SetPlayer(SiginJson data)
	{
		this.id = data.id;
		this.token = data.token;

		Debug.Log("протокол - "+ data.protocol);

		connect = (Protocol)Activator.CreateInstance(Type.GetType(data.protocol));
		connect.Send("{\"token\": \"" + data.token + "\", \"action\": \"api/load\"}");
	}

	/// <summary>
	/// Обработка пришедших от сервера значений
	/// </summary>
	/// <param name="recive">JSON сигнатура согласно стрктуре ReciveJson</param>
	private void HandleData(ReciveJson recive)
	{
		if (recive.action == "screen")
		{
			StartCoroutine(Screen());
		}		
		else if (recive.error != null)
		{
			// todo можно не сбраывать соединение а просто выводить что ошибка
			StartCoroutine(LoadRegister("Ошибка сервера:" + recive.error));
		}
		else
		{
			// если есть объекты
			if (recive.map.data != null)
			{
				map.GetComponent<SpriteRenderer>().sprite = ImageToSpriteModel.Base64ToSprite(recive.map.data);
			}

			if (recive.players != null)
			{
				foreach (PlayerJson player in recive.players)
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

					prefab.GetComponent<PlayerModel>().SetData(player);

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
				foreach (EnemyJson enemy in recive.enemys)
				{
					GameObject prefab = GameObject.Find("enemy_" + enemy.id);
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + enemy.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "enemy_" + enemy.id;
					}

					prefab.GetComponent<EnemyModel>().SetData(enemy);
				}
			}

			// если есть объекты
			if (recive.objects != null)
			{
				foreach (ObjectJson obj in recive.objects)
				{
					GameObject prefab = GameObject.Find("enemy_" + obj.id);
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + obj.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "object_" + obj.id;
					}

					prefab.GetComponent<ObjectModel>().SetData(obj);
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
		Protocol.error = null;
		Protocol.recives.Clear();

		if (loading)
			yield break;

		loading = true;
		Debug.LogError(error);

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
}