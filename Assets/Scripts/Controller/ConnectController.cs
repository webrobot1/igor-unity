using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

using WebGLSupport;

/// <summary>
/// Класс для обработки запросов, конект
/// </summary>
public abstract class ConnectController : BaseController
{
	/// <summary>
	/// максимальное количество секунд системной паузы
	/// </summary>
	const int PAUSE_SECONDS = 10;

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
	private string player_key;

	/// <summary>
	/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
	/// </summary>
	private string token;	

	[SerializeField]
	private Cinemachine.CinemachineVirtualCamera camera;

	/// <summary>
	/// объект в котором будут дочерние объекты карт
	/// </summary>
	[SerializeField]
	private GameObject mapObject;
		
	/// <summary>
	/// родителький объект всех обектов
	/// </summary>
	[SerializeField]
	private GameObject worldObject;

	/// <summary>
	/// массив декодированных с сервера карт
	/// </summary>
	public Dictionary<string, MapDecode> maps = new Dictionary<string, MapDecode>();

	/// <summary>
	/// массив с перечнем с какой стороны какая смежная карта
	/// </summary>
	public Dictionary<string, int> sides = new Dictionary<string, int>();


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

	// повторная загрузка всего пира по новой при переключении между вкладками браузера
	// если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
	// TODO придумать как отказаться от этого
	private void Load()
	{
		if (connect == null) return;

		SigninResponse response = new SigninResponse();
		response.action = "load/index";

		connect.Send(response);
	}

	/// <summary>
	/// Звпускается после авторизации - заполяет id и token 
	/// </summary>
	/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
	public void SetPlayer(SigninRecive data)
	{
		this.player_key = data.key;
		this.token = data.token;

		if(mapObject == null)
			Error("не присвоен GameObject для карт");		
		
		if(worldObject == null)
			Error("не присвоен GameObject для игровых обектов");

		connect = new Websocket(data.host, this.player_key, this.token);
	}

	private IEnumerator GetMap(string side)
	{
		if (mapObject.transform.Find(side) != null)
			Error("карта " + side + " уже выгружена в игровое пространство");
		else if(maps.ContainsKey(side))
			Error("попытка загрузки карты "+side+" повторно");
		else
		{
			if (side != "center")
			{				
				if (mapObject.transform.Find("center") == null)
				{
					Error("не загружена центральная карта для загрузки смежных");
					yield break;
				}
			}

			WWWForm formData = new WWWForm();
			formData.AddField("token", this.token);
			formData.AddField("side", side);

			string url = "http://" + SERVER + "/server/signin/get_map";
			Debug.Log("получаем карту "+side+" с " + url);

			UnityWebRequest request = UnityWebRequest.Post(url, formData);

			yield return request.SendWebRequest();

			// проверим что пришло в ответ
			string text = request.downloadHandler.text;
			if (text.Length > 0)
			{
				Debug.Log("Ответ от сервера карт "+ text);

				try
				{
					MapDecodeRecive recive = JsonConvert.DeserializeObject<MapDecodeRecive>(text);

					if (recive.error.Length > 0)
					{
						Error("Ошибка запроса карты "+ side + ": "+recive.error);
					}
					else if(recive.map.Length>0)
					{
						Debug.Log("Обновляем карту " + side);

						Transform grid = new GameObject(side).transform;
						grid.gameObject.AddComponent<Grid>();
						grid.SetParent(mapObject.transform, false);

						// приведем координаты в сответсвие с сеткой Unity
						try
						{
							maps.Add(side, MapDecodeModel.generate(recive.map, grid, camera));
							SortMap();

							if (side == "center")
							{
								if (maps["center"].map_id == 1)
									StartCoroutine(GetMap("right"));
								else
									StartCoroutine(GetMap("left"));
							}
						}
						catch (Exception ex)
						{
							Debug.LogException(ex);
							Error("Ошибка разбора карты");
						}
					}
					else
						Error("ответ запроса сервера карт не содержит карту");
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
					Error("Ошибка запроса карты");
				}
			}
			else
				Error("Пустой ответ сервера карт  " + request.error);
		}
	}

	public void SortMap()
    {
		foreach (Transform grid in mapObject.transform)
		{
			string side = grid.gameObject.name;
			switch (side)
			{
				case "center":
					grid.localPosition = new Vector3(0f, 0f, 0f);
				break;	
					
				case "right":
					grid.position = new Vector3(grid.position.x + maps["center"].width, grid.position.y, grid.position.z);
				break;

				case "left":
					grid.position = new Vector3(grid.position.x - maps[side].width, grid.position.y, grid.position.z);
				break;
			}

			// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
			if (worldObject.transform.Find(side) != null)
			{
				foreach (Transform child in worldObject.transform.Find(side))
				{
					if (child.gameObject.GetComponent<SpriteRenderer>())
						child.gameObject.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[side].spawn_sort + (int)child.GetComponent<ObjectModel>().sort;

					if (child.gameObject.GetComponentInChildren<Canvas>())
						child.gameObject.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[side].spawn_sort + 1 + (int)child.GetComponent<ObjectModel>().sort;
				}
			}
		}
	}

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	protected void FixedUpdate()
	{
		if (connect == null)
			return;
		else if (connect.reconnect>0)
        {
			if(connect.reconnect == 1)
            {
				connect.reconnect = 2;
				StartCoroutine(HttpRequest("auth"));
			}				
		}
		else if(Websocket.errors.Count > 0)
			StartCoroutine(LoadRegister());
		else if (connect.pause!=null)
        {
			if (DateTime.Compare(((DateTime)connect.pause).AddSeconds(PAUSE_SECONDS), DateTime.Now) < 1)
				Error("Слишком долгая системная пауза");
			else
				Debug.Log("Пауза");
		}
		else //if (spawn_sort["center"]!=null)  // обрабатываем пакеты если уже загрузился карта
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
						Debug.LogException(ex);
						Error("Ошибка разбора входящих данных");
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
        if (recive.action != null) 
		{ 
			switch (recive.action)
			{	
				case "load/index":

					// удаляет не сразу а на следующем кадре все карты
					// главное не через for  от количества детей делать DestroyImmediate - тк количество детей пропорционально будет уменьшаться
					foreach (var child in worldObject.transform.Cast<Transform>().ToList())
					{
						DestroyImmediate(child.gameObject);
					}
					Debug.Log("полная перезагрузка мира");
				break;
			}
		}
		Debug.Log("Обрабатываем данные");

		if (recive.sides != null)
		{
			// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
			if (this.maps.Count > 0)
			{
				bool find;
				Dictionary<string, MapDecode> new_maps = new Dictionary<string, MapDecode>();

				foreach (KeyValuePair<string, int> side in recive.sides)
				{
					find = false;
					foreach (KeyValuePair<string, MapDecode> map in maps)
					{
						if (map.Value.map_id == side.Value)
						{
							if (map.Key != side.Key)
							{
								Debug.Log("подмена карты");
								mapObject.transform.Find(map.Key).gameObject.name = side.Key;
								new_maps.Add(side.Key, map.Value);
							}
							find = true;
						}
					}

					if (!find && mapObject.transform.Find(side.Key))
						DestroyImmediate(mapObject.transform.Find(side.Key).gameObject);
				}

				if (new_maps.Count > 0)
                {
					maps = new_maps;
					SortMap();
				}	
			}

			if (!mapObject.transform.Find("center"))
            {
				if (recive.sides.ContainsKey("center"))
				{
					StartCoroutine(GetMap("center"));
				}
				else
					Error("Зпись о центральной карте не пришла");
			}
				
			this.sides = recive.sides;
		}

		if (recive.world != null)
		{
			foreach (KeyValuePair<string, MapRecive> map in recive.world)
			{
				// найдем карту на сцене для которых пришло обнолление. если пусто - создадим ее
				Transform? map_zone = worldObject.transform.Find(map.Key);
				if (map_zone == null)
				{
					map_zone = new GameObject(map.Key).transform;
					map_zone.SetParent(worldObject.transform, false);

					Debug.LogWarning("Создаем область для объектов "+ map.Key);
				}

				// если пришел пустой обхект (массив)  то надо все удалить с зоны карты все электменты 
				// todo более изящного и короткого способа сравнения объектов (рабочего)  я не нашел
				if (JsonConvert.SerializeObject(map.Value) == JsonConvert.SerializeObject(new MapRecive()))
                {
					Debug.LogWarning("локация "+map.Key+" отправила пустое содержимое - удалим ее объекты с карты");
					
					// если саму зону оставить надо
/*					foreach (var child in map_zone.Cast<Transform>().ToList())
					{
						DestroyImmediate(child.gameObject);
					}*/

					DestroyImmediate(map_zone.gameObject);
				}
				else
				{ 
					if (map.Value.players != null)
					{
						Debug.Log("Обновляем игроков");
						foreach (KeyValuePair<string, PlayerRecive> player in map.Value.players)
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
								prefab.transform.SetParent(map_zone.transform, false);

								if (player.Key == player_key)
								{
									camera.Follow = prefab.transform;
									this.player = prefab.GetComponent<PlayerModel>();

									// если у нас webgl првоерим не а дминке ли мы с API отладкой
									#if UNITY_WEBGL && !UNITY_EDITOR
										WebGLDebug.Check(player.Value.map_id);
									#endif
								}
							}
							else
								Debug.Log("Обновляем " + player.Key);

							// мы сортировку устанавливаем в двух местах - здесь и при загрузке карты. тк объекты могут быть загружены раньше карты и наоборот
							if (maps.ContainsKey(map.Key) && player.Value.sort != null)
							{
								if (prefab.GetComponent<SpriteRenderer>())
									prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[map.Key].spawn_sort + (int)player.Value.sort;
								if (prefab.GetComponentInChildren<Canvas>())
									prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[map.Key].spawn_sort + 1 + (int)player.Value.sort;
							}

							try
							{
								prefab.GetComponent<PlayerModel>().SetData(player.Value);
							}
							catch (Exception ex)
							{
								Debug.LogException(ex);
								Error("Не удалось загрузить игрока " + player.Key);
							}
						}
					}

					// если есть враги
					if (map.Value.enemys != null)
					{
						Debug.Log("Обновляем enemy");

						foreach (KeyValuePair<string, EnemyRecive> enemy in map.Value.enemys)
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
								prefab.transform.SetParent(map_zone.transform, false);
							}
							else
								Debug.Log("Обновляем " + enemy.Key);

							if (maps.ContainsKey(map.Key) && enemy.Value.sort != null)
							{
								if (prefab.GetComponent<SpriteRenderer>())
									prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[map.Key].spawn_sort + (int)enemy.Value.sort;
								if (prefab.GetComponentInChildren<Canvas>())
									prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[map.Key].spawn_sort + 1 + (int)enemy.Value.sort;
							}

							try
							{
								prefab.GetComponent<EnemyModel>().SetData(enemy.Value);
							}
							catch (Exception ex)
							{
								Debug.LogException(ex);
								Error("Не удалось загрузить врага " + enemy.Key);
							}
						}
					}

					// если есть объекты
					if (map.Value.objects != null)
					{
						Debug.Log("Обновляем объекты");
						foreach (KeyValuePair<string, ObjectRecive> obj in map.Value.objects)
						{
							GameObject prefab = GameObject.Find(obj.Key);
							if (prefab == null)
							{
								// данные от объектов что могут влиять на игру ДО загрузки сцены (те между sign и load)
								if (obj.Value.prefab == null || obj.Value.prefab.Length == 0)
								{
									continue;
								}

								Debug.Log("Создаем " + obj.Value.prefab + " " + obj.Key);

								UnityEngine.Object? ob = Resources.Load("Prefabs/Objects/" + obj.Value.prefab, typeof(GameObject));

								if (ob == null)
									ob = Resources.Load("Prefabs/Objects/Empty", typeof(GameObject));

								prefab = Instantiate(ob) as GameObject;
								prefab.name = obj.Key;

								//todo сделать слой объектов
								prefab.transform.SetParent(map_zone.transform, false);
							}
							else
								Debug.Log("Обновляем "+obj.Key);

							if (maps.ContainsKey(map.Key) && obj.Value.sort!=null)
							{
								if(prefab.GetComponent<SpriteRenderer>())
									prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[map.Key].spawn_sort + (int)obj.Value.sort;
								if(prefab.GetComponentInChildren<Canvas>())
									prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[map.Key].spawn_sort + 1 + (int)obj.Value.sort;
							}

							try
							{
								if (prefab.GetComponent<ObjectModel>())
									prefab.GetComponent<ObjectModel>().SetData(obj.Value);
							}
							catch (Exception ex)
							{
								Debug.LogException(ex);
								Error("Не удалось загрузить объект " + obj.Key);
							}
						}
					}
				}
			}
		}
	}

	public override void Error (string text)
    {
		Websocket.errors.Add(text);
		StartCoroutine(LoadRegister());

		throw new Exception(text);
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
		Camera.main.GetComponent<SigninController>().Error(String.Join(", ", Websocket.errors));
	}


	void OnApplicationQuit()
	{
		Debug.Log("Закрытие приложения");

		if(connect!=null)
			connect.Close();
	}
}