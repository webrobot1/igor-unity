using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

using WebGLSupport;

/// <summary>
/// Класс для обработки запросов, конект
/// </summary>
public abstract class ConnectController : MainController
{
	/// <summary>
	/// Ссылка на конектор
	/// </summary>
	protected Websocket connect;

	/// <summary>
	/// Префаб нашего игрока
	/// </summary>
	protected PlayerModel player;

	/// <summary>
	/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (нужен только между методом Sign и Load)
	/// </summary>
	private int id;

	/// <summary>
	/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
	/// </summary>
	private string token;	

	[SerializeField]
	private Cinemachine.CinemachineVirtualCamera camera;

	/// <summary>
	/// тайловая сетка карты
	/// </summary>
	[SerializeField]
	private GameObject grid;
		
	/// <summary>
	/// родителький объект всех обектов
	/// </summary>
	[SerializeField]
	private GameObject world;

	/// <summary>
	/// на каком уровне слоя размещать новых персонажей и npc и на каком следит камера
	/// </summary>
	public static int? spawn_sort = null;


#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
	public void OnApplicationPause(bool pause)
	{
		Debug.Log("Пауза " + pause);

		if(!pause)
			Load();
	}
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
	public void OnApplicationFocus(bool focus)
	{
		Debug.Log("фокус " + focus);

		if(focus)
			Load();
	}

	public void Api(string json)
	{
		connect.Put(json);
	}
#endif

    /// <summary>
    /// Звпускается после авторизации - заполяет id и token 
    /// </summary>
    /// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
    public void SetPlayer(SiginRecive data)
	{
		id = data.id;
		this.token = data.token;

		connect = new Websocket(WEBSOCKET_SERVER, data.port, id, this.token);

		// настройки size камеры менять бессмысленно тк есть PixelPerfect
		// но и менять assetsPPU  тоже нет смысла тк на 16х16 у нас будет нужное нам отдаление (наприме)  а на 32х32 меняя assetsPPU все станет гиганским
		/*
		GetComponent<Camera>().orthographicSize = GetComponent<Camera>().orthographicSize * 16 / this.PixelsPerUnit;
		GetComponent<UnityEngine.U2D.PixelPerfectCamera>().assetsPPU = (int)this.PixelsPerUnit;
		*/

		Debug.Log("Обновляем карту");

		// удалим  все слои что были ранее
		// оставим тут а ре в Load тк возможно что будет отправлять через Load при перезагруке мира все КРОМЕ карты поэтому ее не надо зачищать если не придет новая
		for (int i = 0; i < grid.transform.parent.childCount; i++)
		{
			if (grid.transform.parent.GetChild(i).gameObject.GetInstanceID() != grid.GetInstanceID())
				DestroyImmediate(grid.transform.parent.GetChild(i).gameObject);
		}

        // приведем координаты в сответсвие с сеткой Unity
        try {
			spawn_sort = MapModel.getInstance().generate(ref data.map, grid, camera);
		}
		catch (Exception ex)
		{
			Websocket.errors.Add("Ошибка разбора карты " + ex.Message);
			StartCoroutine(LoadRegister());
		}
	}

	// если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
	private void Load()
    {
		if (connect == null) return;

		SigninResponse response = new SigninResponse();
		response.action = "load/index";

		connect.Send(response);
	}

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	protected void FixedUpdate()
	{
		if (connect == null)
			return;
		else if(Websocket.errors.Count > 0)
			StartCoroutine(LoadRegister());
		else if (connect.pause)
			Debug.Log("Пауза");
		else if (spawn_sort != null)  // обрабатываем пакеты если уже загрузился карта
		{
			// тк в процессе разбора могут появиться новые данные то обработаем только те что здесь и сейчас были
			int count = connect.recives.Count;
			if (count > 0)
			{
				for (int i = 0; i < count; i++)
				{
					try
					{
						HandleData(connect.recives[i]);
					}
					catch (Exception ex)
					{
						Websocket.errors.Add("Ошибка разбора входящих данных, " + ex.Message);
						StartCoroutine(LoadRegister());
						break;
					}
				}

				// и удалим только те что обработали (хотя могли прийти и новые пока обрабатвали, но это уже в следующем кадре)
				connect.recives.RemoveRange(0, count);
			}
		}
	}

	/// <summary>
	/// Обработка пришедших от сервера значений
	/// </summary>
	/// <param name="recive">JSON сигнатура согласно стрктуре ReciveJson</param>
	private void HandleData(Recive recive)
	{
        switch (recive.action)
        {
			case "screen/index":
				StartCoroutine(Screen());
			break;
				
			case "load/index":

				// удаляет не сразу а на следующем кадре
				for (int i = 0; i < world.transform.childCount; i++)
				{
					DestroyImmediate(world.transform.GetChild(i).gameObject);
				}
				Debug.Log("перезагрузка мира");

			break;
        }

		Debug.Log("Обрабатываем данные");

		if (recive.players != null)
		{
			Debug.Log("Обновляем игроков");
			foreach (KeyValuePair<string, PlayerRecive> player in recive.players)
			{
				GameObject prefab = GameObject.Find(player.Key);

				// если игрока нет на сцене
				if (prefab == null)
				{
					// если игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
					if (player.Value.prefab == null || player.Value.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + player.Value.prefab + " " + player.Key);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Players/" + player.Value.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Players/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = player.Key;
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				
					if (player.Value.id == id)
					{
						//transform.SetParent(prefab.transform);
						//transform.position = new Vector3(transform.parent.position.x, transform.parent.position.y, transform.position.z);
						camera.Follow = prefab.transform;
						this.player = prefab.GetComponent<PlayerModel>();

						// если у нас webgl првоерим не а дминке ли мы с API отладкой
#if UNITY_WEBGL && !UNITY_EDITOR
							WebGLDebug.Check(player.Value.map_id);
#endif
					}
				}

				try
				{
					prefab.GetComponent<PlayerModel>().SetData(player.Value);
				}
				catch (Exception ex)
				{
					Websocket.errors.Add("Не удалось загрузить игрока " + player.Key + " :" + ex);
					StartCoroutine(LoadRegister());
				}
			}
		}

		// если есть враги
		if (recive.enemys != null)
		{
			Debug.Log("Обновляем enemy");

			foreach (KeyValuePair<string, EnemyRecive> enemy in recive.enemys)
			{
				GameObject prefab = GameObject.Find(enemy.Key);
				if (prefab == null)
				{
						
					// данные от NPC что могут уже атаковать ДО загрузки сцены (те между sign и load)
					if (enemy.Value.prefab == null || enemy.Value.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + enemy.Value.prefab + " "+ enemy.Key);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Enemys/" + enemy.Value.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Enemys/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = enemy.Key;
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				}
				else
					Debug.Log("Обновляем " + enemy.Key);

				try
				{
					prefab.GetComponent<EnemyModel>().SetData(enemy.Value);
				}
				catch (Exception ex)
				{
					Websocket.errors.Add("Не удалось загрузить врага " + enemy.Key + " :" + ex);
					StartCoroutine(LoadRegister());
				}
			}
		}

		// если есть объекты
		if (recive.objects != null)
		{
			Debug.Log("Обновляем объекты");
			foreach (KeyValuePair<string, ObjectRecive> obj in recive.objects)
			{
				GameObject prefab = GameObject.Find(obj.Key);
				if (prefab == null)
				{
					// данные от объектов что могут влиять на игру ДО загрузки сцены (те между sign и load)
					if (obj.Value.prefab == null || obj.Value.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + obj.Value.prefab + " "+ obj.Key);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Objects/" + obj.Value.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Objects/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = obj.Key;

					//todo сделать слой объектов

					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				}

				try
				{
					if (prefab.GetComponent<ObjectModel>())
						prefab.GetComponent<ObjectModel>().SetData(obj.Value);
				}
				catch (Exception ex)
				{
					Websocket.errors.Add("Не удалось загрузить объект " + obj.Key + ": " + ex);
					StartCoroutine(LoadRegister());
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

		UnityWebRequest request = UnityWebRequest.Post("http://"+SERVER+"/game/signin/screen", form);

		yield return request.SendWebRequest();

		if (request.result != UnityWebRequest.Result.Success)
		{
			Debug.LogError(request.error);
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
	private IEnumerator LoadRegister()
	{
		if (connect == null)
		{
			Debug.LogWarning("уже закрываем игру");
			yield break;
		}
        else
        {
			connect.Close();
			connect = null;
		}
			
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
		Camera.main.GetComponent<RegisterController>().Error(String.Join(", ", Websocket.errors));
	}


	void OnApplicationQuit()
	{
		Debug.Log("Закрытие приложения");

		if(connect!=null)
			connect.Close();
	}
}