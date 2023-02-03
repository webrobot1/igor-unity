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

namespace MyFantasy
{
	/// <summary>
	/// Класс для обработки запросов, конект
	/// </summary>
	public abstract class ConnectController : BaseController
	{
		/// <summary>
		/// Префаб нашего игрока
		/// </summary>
		public dynamic playerModel;

		/// <summary>
		/// максимальное количество секунд системной паузы
		/// </summary>
		const int PAUSE_SECONDS = 10;

		/// <summary>
		/// Ссылка на конектор
		/// </summary>
		protected Websocket connect;

		/// <summary>
		/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (нужен только между методом Sign и Load)
		/// </summary>
		private string player_key;

		/// <summary>
		/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
		/// </summary>
		private string token;	

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

			Response response = new Response();
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

			Debug.Log(GetReciveStruct());

			connect = new Websocket(GetReciveStruct(), data.host, this.player_key, this.token);
		}

		/// <summary>
		/// какая структура Recive (можно переопределить) должна обрабатывать поступивший от сервера ответ
		/// </summary>
		protected virtual Recive GetReciveStruct()
        {
			return new Recive();
        }

		private IEnumerator GetMap(string side)
		{
			if (mapObject.transform.Find(side) != null)
				Error("карта " + side + " уже выгружена в игровое пространство");
			else if(maps.ContainsKey(side))
				Error("попытка загрузки карты "+side+" повторно");
			else
			{
			   /* if (side != "center")
				{				
					if (mapObject.transform.Find("center") == null)
					{
						Error("не загружена центральная карта для загрузки");
						yield break;
					}
				}*/

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
								maps.Add(side, MapDecodeModel.generate(recive.map, grid));
								SortMap();
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

						// если у нас webgl првоерим не а дминке ли мы с API отладкой
						#if UNITY_WEBGL && !UNITY_EDITOR
							WebGLDebug.Check(maps[side].map_id);
						#endif

						grid.localPosition = new Vector3(0f, 0f, 0f);
					break;	
						
					case "right":
						// центральная карта может отсутвуовать например когда мы ушли с одной карты на другую и переиспользовали графику (SortMap  запустится но оттуда откуда пришли могло не быть графики карты куда пришли)
						if (maps.ContainsKey("center"))
							grid.localPosition = new Vector3(maps["center"].width, 0, 0);
					break;

					case "left":
						grid.localPosition = new Vector3(maps[side].width*-1, 0, 0);
					break;
				}

				// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
				if (worldObject.transform.Find(side) != null)
				{
					foreach (Transform child in worldObject.transform.Find(side))
					{
						dynamic model = child.GetComponent("ObjectModel");
						if (model != null)
						{

							if (child.gameObject.GetComponent<SpriteRenderer>())
								child.gameObject.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[side].spawn_sort + (int)model.sort;

							if (child.gameObject.GetComponentInChildren<Canvas>())
								child.gameObject.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[side].spawn_sort + 1 + (int)model.sort;
						}
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
		protected void HandleData(dynamic recive)
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
				if (!recive.sides.ContainsKey("center")) Error("Запись о центральной карте не пришла");
				this.sides = recive.sides;

				// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
				if (this.maps.Count > 0)
				{
					bool find;
					Dictionary<string, MapDecode> new_maps = new Dictionary<string, MapDecode>();

					// сначала 
					foreach (KeyValuePair<string, int> side in this.sides)
					{
						find = false;
						foreach (KeyValuePair<string, MapDecode> map in maps)
						{
							if (map.Value.map_id == side.Value)
							{
								if (map.Key != side.Key)
								{
									Debug.Log("подмена карты с "+ map.Key + " на "+ side.Key);
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

				// загрузим отвутвующую графику центральной и смежных карт 
				// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
				foreach (KeyValuePair<string, int> side in this.sides)
				{
					if (!maps.ContainsKey(side.Key)) StartCoroutine(GetMap(side.Key));
				}
			}

			if (recive.world != null)
			{	
				foreach (var map in recive.world)
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
							foreach (var player in map.Value.players)
							{
								UpdateObject(map.Key, player.Key, player.Value, "Players");
							}
						}

						// если есть враги
						if (map.Value.enemys != null)
						{
							Debug.Log("Обновляем enemy");

							foreach (var enemy in map.Value.enemys)
							{
								UpdateObject(map.Key, enemy.Key, enemy.Value, "Enemys");
							}
						}

						// если есть объекты
						if (map.Value.objects != null)
						{
							Debug.Log("Обновляем объекты");
							foreach (var obj in map.Value.objects)
							{
								UpdateObject(map.Key, obj.Key, obj.Value, "Objects");
							}
						}
					}
				}
			}
		}

		private void UpdateObject(string side, string key, dynamic value, string type)
        {
			GameObject prefab = GameObject.Find(key);

			// если игрока нет на сцене
			if (prefab == null)
			{
				// если игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
				if (value.prefab == null || value.prefab.Length == 0)
				{
					return;
				}

				Debug.Log("Создаем " + value.prefab + " " + key);

				UnityEngine.Object? ob = Resources.Load("Prefabs/"+type+"/" + value.prefab, typeof(GameObject));

				if (ob == null)
					ob = Resources.Load("Prefabs/" + type + "/Empty", typeof(GameObject));

				prefab = Instantiate(ob) as GameObject;
				prefab.name = key;
				prefab.transform.SetParent(worldObject.transform.Find(side).transform, false);

				if (key == player_key)
				{
					this.playerModel = prefab.GetComponent("ObjectModel");
				}
			}
			else
				Debug.Log("Обновляем " + key);

			// мы сортировку устанавливаем в двух местах - здесь и при загрузке карты. тк объекты могут быть загружены раньше карты и наоборот
			if (maps.ContainsKey(side) && value.sort!=null)
			{
				if (prefab.GetComponent<SpriteRenderer>())
					prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)maps[side].spawn_sort + value.sort;
				if (prefab.GetComponentInChildren<Canvas>())
					prefab.GetComponentInChildren<Canvas>().sortingOrder = (int)maps[side].spawn_sort + 1 + value.sort;
			}

			try
			{
				dynamic model = prefab.GetComponent("ObjectModel");

				Debug.Log(model);
				Debug.Log(value);

				if (model != null)
					model.SetData(value);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				Error("Не удалось загрузить игрока " + key);
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
			Camera.main.GetComponent<BaseController>().Error(String.Join(", ", Websocket.errors));
		}


		void OnApplicationQuit()
		{
			Debug.Log("Закрытие приложения");

			if(connect!=null)
				connect.Close();
		}
	}
}