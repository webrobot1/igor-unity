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
	private int? id = null;

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
		Time.fixedDeltaTime = data.time;

		Debug.Log("FixedTime = " + data.time);

		connect = new Websocket(SERVER, PORT, data.map_id);

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
				Destroy(grid.transform.parent.GetChild(i).gameObject);
		}

        // приведем координаты в сответсвие с сеткой Unity
        try {
			Load(data.token);
			spawn_sort = MapModel.getInstance().generate(ref data.map, grid, camera);
		}
		catch (Exception ex)
		{
			StartCoroutine(LoadRegister("Ошибка разбора карты " + ex.Message));
		}
	}

	// если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
	private void Load(string token = "")
    {
		if (connect == null) return;

		SigninResponse response = new SigninResponse();
		response.action = "load/index";
		if(token.Length>0)
			response.token = token;

		connect.Send(response);
	}

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	protected void FixedUpdate()
	{
		if (connect == null)
			return;
		else if(connect.error.Length > 0)
			StartCoroutine(LoadRegister(connect.error));
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
						StartCoroutine(LoadRegister("Ошибка разбора входящих данных, " + ex.Message));
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

				// имено тут если делать там же где и расставляем объекты будут ...закешированы (те будут находится по поиску по имени)
				for (int i = 0; i < world.transform.childCount; i++)
				{
					Destroy(world.transform.GetChild(i).gameObject);
				}
				Debug.Log("перезагрузка мира");

			break;
        }

		Debug.Log("Обрабатываем данные");

		if (recive.players != null)
		{
			Debug.Log("Обновляем игроков");
			foreach (PlayerRecive player in recive.players)
			{
				string name = "player_" + player.id;
				GameObject prefab = GameObject.Find(name);

				// если игрока нет на сцене
				if (prefab == null)
				{
					// если игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
					if (player.prefab == null || player.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + player.prefab + " " + name);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Players/" + player.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Players/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = name;
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				
					if (player.id == id)
					{
						//transform.SetParent(prefab.transform);
						//transform.position = new Vector3(transform.parent.position.x, transform.parent.position.y, transform.position.z);
						camera.Follow = prefab.transform;
						this.player = prefab.GetComponent<PlayerModel>();

						// если у нас webgl првоерим не а дминке ли мы с API отладкой
#if UNITY_WEBGL && !UNITY_EDITOR
							WebGLDebug.Check(this.token, player.map_id);
#endif
					}
				}

				try
				{
					prefab.GetComponent<PlayerModel>().SetData(player);
				}
				catch (Exception ex)
				{
					StartCoroutine(LoadRegister("Не удалось загрузить игрока " + name + " :" + ex));
				}
			}
		}

		// если есть враги
		if (recive.enemys != null)
		{
			Debug.Log("Обновляем enemy");

			foreach (EnemyRecive enemy in recive.enemys)
			{
				
				string name = "enemy_" + enemy.id; 
				GameObject prefab = GameObject.Find(name);
				if (prefab == null)
				{
						
					// данные от NPC что могут уже атаковать ДО загрузки сцены (те между sign и load)
					if (enemy.prefab == null || enemy.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + enemy.prefab + " "+ name);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Enemys/" + enemy.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Enemys/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = name;
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				}
				else
					Debug.Log("Обновляем " + name);

				try
				{
					prefab.GetComponent<EnemyModel>().SetData(enemy);
				}
				catch (Exception ex)
				{
					StartCoroutine(LoadRegister("Не удалось загрузить NPC " + name + " :" + ex));
				}
			}
		}

		// если есть объекты
		if (recive.objects != null)
		{
			Debug.Log("Обновляем объекты");
			foreach (ObjectRecive obj in recive.objects)
			{
				string name = "object_" + obj.id;
				GameObject prefab = GameObject.Find(name);
				if (prefab == null)
				{
					// данные от объектов что могут влиять на игру ДО загрузки сцены (те между sign и load)
					if (obj.prefab == null || obj.prefab.Length == 0)
					{
						continue;
					}

					Debug.Log("Создаем " + obj.prefab + " "+name);

					UnityEngine.Object? ob = Resources.Load("Prefabs/Objects/" + obj.prefab, typeof(GameObject));

					if (ob == null)
						ob = Resources.Load("Prefabs/Objects/Empty", typeof(GameObject));

					prefab = Instantiate(ob) as GameObject;
					prefab.name = name;

					//todo сделать слой объектов

					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)spawn_sort;
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)spawn_sort + 1;
					prefab.transform.SetParent(world.transform, false);
				}

				try
				{
					if (prefab.GetComponent<ObjectModel>())
						prefab.GetComponent<ObjectModel>().SetData(obj);
				}
				catch (Exception ex)
				{
					StartCoroutine(LoadRegister("Не удалось загрузить объект " + name + ": " + ex));
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

		UnityWebRequest request = UnityWebRequest.Post("http://my-fantasy.ru/game/signin/screen", form);

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
	private IEnumerator LoadRegister(string error)
	{
		Debug.LogError(error);

		if (connect == null)
		{
			Debug.LogWarning("уже закрываем игру ("+ error + ")");
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
		Camera.main.GetComponent<RegisterController>().Error(error);
	}


	void OnApplicationQuit()
	{
		Debug.Log("Закрытие приложения");

		if(connect!=null)
			connect.Close();
	}
}