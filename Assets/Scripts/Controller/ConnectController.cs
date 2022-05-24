using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using WebGLSupport;

/// <summary>
/// Класс для обработки запросов, конект
/// </summary>
public abstract class ConnectController : MainController
{
	/// <summary>
	/// Ссылка на конектор
	/// </summary>
	public static Protocol connect;

	/// <summary>
	/// Префаб нашего игрока
	/// </summary>
	public static PlayerModel player;

	/// <summary>
	/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте (нужен только между методом Sign и Load)
	/// </summary>
	private int? id = null;

	/// <summary>
	/// true - загружается сцена регистрации (выходим из игры)
	/// </summary>
	private bool exit;

	/// <summary>
	/// true - пауза (выходим, входим или перезагружаем мир игры)
	/// </summary>
	public static bool pause;

	/// <summary>
	/// Токен , требуется при первом конекте для Tcp и Ws, и постоянно при Udp
	/// </summary>
	private string token;

	/// <summary>
	/// время от нажатия кнопки идти до ответа сервера (переделать в List)
	/// </summary>
	protected double pingTime;

	/// <summary>
	/// сколько пикселей на 1 Unit должно считаться (размер клетки)
	/// </summary>
	private float PixelsPerUnit;

	/// <summary>
	/// время последнего шага нашего игрока (если null то шаг закончен)
	/// </summary>
	protected DateTime lastMove = DateTime.Now;

	/// <summary>
	/// координата на карте к которой мы движемся по клику мыши (или пок аким то другим принудительным действиям)
	/// </summary>
	protected Vector2 moveTo = Vector2.zero;

	/// <summary>
	/// Позиция к которой движется наш персонаж (пришла от сервера)
	/// </summary>
	protected Vector2 target;	
	

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
	/// на каком уровне слоя размещать новых персонажей и npc 
	/// </summary>
	private int? ground_sort = null;


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
#endif

	private void Load(string token = "")
    {
		Debug.LogError("загрузка мира");
		
		connect.recives.Clear();

		// имено тут если делать там же где и расставляем объекты будут ...закешированы (те будут находится по поиску по имени)
		for (int i = 0; i < world.transform.childCount; i++)
		{
			Destroy(world.transform.GetChild(i).gameObject);
		}

		SigninResponse response = new SigninResponse();
		response.action = "load";
		if(token.Length>0)
			response.token = token;

		connect.Send(response);

		// поставим на паузу отправку любых данных
		pause = true;

		Debug.Log(connect.recives);
	}

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	protected void Update()
	{
		if (connect != null)
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
						Debug.Log(DateTime.Now.Millisecond + ": " + connect.recives[i]);
						HandleData(JsonConvert.DeserializeObject<Recive>(connect.recives[i]));
						
						if(connect.recives.ElementAtOrDefault(i) != null)
							connect.recives.RemoveAt(i);
					}
					catch (Exception ex)
					{
						StartCoroutine(LoadRegister("Ошибка разбора входящих данных, " + ex.Message + ": " + connect.recives[i]));
						break;
					}
				}
			}
		}
		else if(id == null)
			LoadRegister("Неверный порядок запуска сцен");
		else
			LoadRegister("Соединение потеряно");
	}


	/// <summary>
	/// Звпускается после авторизации - заполяет id и token 
	/// </summary>
	/// <param name="data">Json сигнатура данных авторизации согласно SiginJson</param>
	public void SetPlayer(SiginRecive data)
	{
		id = data.id;
		this.token = data.token;
		this.PixelsPerUnit = data.pixels;
		this.pingTime = Time.fixedDeltaTime = data.time;

		Debug.Log("FixedTime = " + data.time);
		connect = new Websocket();

		// настройки size камеры менять бессмысленно тк есть PixelPerfect
		// но и менять assetsPPU  тоже нет смысла тк на 16х16 у нас будет нужное нам отдаление (наприме)  а на 32х32 меняя assetsPPU все станет гиганским
		/*
		GetComponent<Camera>().orthographicSize = GetComponent<Camera>().orthographicSize * 16 / this.PixelsPerUnit;
		GetComponent<UnityEngine.U2D.PixelPerfectCamera>().assetsPPU = (int)this.PixelsPerUnit;
		*/

		Load(data.token);
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
		else if (!exit && (!pause || recive.action == "load")) // обновляем мир только если в не выходим из игры и не перезагружаем мир (занмиает какое то время)
		{
			Debug.Log("Обрабатываем данные");

			// если есть объекты
			if (recive.map != null)
			{
				// удалим  все слои что были ранее
				// оставим тут а ре в Load тк возможно что будет отправлять через Load при перезагруке мира все КРОМЕ карты поэтому ее не надо зачищать если не придет новая
				for (int i = 0; i < grid.transform.parent.childCount; i++)
				{
					if (grid.transform.parent.GetChild(i).gameObject.GetInstanceID() != grid.GetInstanceID())
						Destroy(grid.transform.parent.GetChild(i).gameObject);
				}

				// приведем координаты в сответсвие с сеткой Unity
				Map map = MapModel.getInstance().decode(recive.map, PixelsPerUnit);

				// инициализируем новый слой 
				GameObject newLayer;
				int sort = 0;

				// расставим на сцене данные карты
				foreach (Layer layer in map.layer)
				{

					newLayer = Instantiate(Resources.Load("Prefabs/Tilemap", typeof(GameObject))) as GameObject;
					newLayer.name = layer.name;
					newLayer.transform.SetParent(grid.transform, false);
					newLayer.GetComponent<TilemapRenderer>().sortingOrder = sort;

					Tilemap tilemap = newLayer.GetComponent<Tilemap>();

					// если есть в слое набор тайлов
					if (layer.tiles != null)
					{
						foreach (KeyValuePair<int, LayerTile> tile in layer.tiles)
						{
							if (tile.Value.tile_id > 0)
							{
								TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

								// если tile отражен по горизонтали или вертикали или у него z параметр (нужно где слои лежить друг за другом по Y)
								if (tile.Value.horizontal > 0 || tile.Value.vertical > 0)
								{
									var m = newTile.transform;
									m.SetTRS(Vector3.zero, Quaternion.Euler(tile.Value.vertical * 180, tile.Value.horizontal * 180, 0f), Vector3.one);
									newTile.transform = m;
								}

								if (map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites != null)
								{
									newTile.sprites = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites;
								}
								else
									newTile.sprite = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprite;

								tilemap.SetTile(new Vector3Int(tile.Value.x, tile.Value.y, 0), newTile);
							}
						}
						Debug.Log(newLayer.name + " раставлены tile");
					}
					else if (layer.objects != null)
					{
						foreach (KeyValuePair<int, LayerObject> obj in layer.objects)
						{
							// если указанный тайл (клетка) не пустая
							if (obj.Value.tile_id > 0)
							{
								TilemapModel newTile = TilemapModel.CreateInstance<TilemapModel>();

								if (obj.Value.horizontal > 0 || obj.Value.vertical > 0)
								{
									var m = newTile.transform;
									m.SetTRS(Vector3.zero, Quaternion.Euler(obj.Value.vertical * 180, obj.Value.horizontal * 180, 0f), Vector3.one);
									newTile.transform = m;
								}

								if (map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprites != null)
								{
									newTile.sprites = map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprites;
								}
								else
									newTile.sprite = map.tileset[obj.Value.tileset_id].tile[obj.Value.tile_id].sprite;

								// сместим координаты абсолютные на расположение главного слоя Map (у нас ноль идет от -180 для GEO расчетов) для получения относительных
								tilemap.SetTile(new Vector3Int((int)(obj.Value.x), (int)(obj.Value.y), 0), newTile);
							}
						}
					}

					// полупрозрачность слоя
					if (layer.opacity < 1f)
					{
						Renderer[] mRenderers = newLayer.GetComponentsInChildren<Renderer>();
						Debug.Log(mRenderers.Length);
						for (int i = 0; i < mRenderers.Length; i++)
						{
							for (int j = 0; j < mRenderers[i].materials.Length; j++)
							{
								Color matColor = mRenderers[i].materials[j].color;
								matColor.a = layer.opacity;
								mRenderers[i].materials[j].color = matColor;
							}
						}
					}

					sort++;

					// если еще не было слоев что НЕ выше чем сам игрок (те очевидно первый такой будет - земля, а следующий - тот на котром надо генеирить игроков и npc)
					// todo - на сервере иметь параметр "Слой игрока" 
					if (ground_sort == null)
					{
						// создадим колайдер для нашей камеры (границы за которые она не смотрит) если слой земля - самый первый (врятли так можно нарисовать что он НЕ на всю карту и первый)
						if (layer.ground == 1)
						{
							newLayer.AddComponent<TilemapCollider2D>().usedByComposite = true;
							newLayer.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
							CompositeCollider2D colider = newLayer.AddComponent<CompositeCollider2D>();
							colider.geometryType = CompositeCollider2D.GeometryType.Polygons;
							camera.GetComponent<Cinemachine.CinemachineConfiner>().m_BoundingShape2D = colider;

							// землю нет нужды индивидуально просчитывать положения тайлов (тк мы за них не заходим и выше по слою)
							newLayer.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Chunk;
						}

						//  текущий слой на котором будем ставить игроков		
						ground_sort = sort;
					}
				}
			}

			if (recive.players != null)
			{
				foreach (PlayerRecive player in recive.players)
				{
					string name = "player_" + player.id;
					GameObject prefab = GameObject.Find(name);

					// если игрока нет на сцене
					if (prefab == null)
					{
						// если игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
						if (player.prefab == "") continue;

						Debug.Log("Создаем " + name);

						prefab = Instantiate(Resources.Load("Prefabs/Players/" + player.prefab, typeof(GameObject))) as GameObject;
						prefab.name = name;

						prefab.GetComponent<SpriteRenderer>().sortingOrder += (int)ground_sort;
						prefab.GetComponentInChildren<Canvas>().sortingOrder += (int)ground_sort + 1;
						prefab.transform.SetParent(world.transform, false);

						if (player.id == id)
						{
							//transform.SetParent(prefab.transform);
							//transform.position = new Vector3(transform.parent.position.x, transform.parent.position.y, transform.position.z);
							camera.Follow = prefab.transform;
							ConnectController.player = prefab.GetComponent<PlayerModel>();

							// если у нас webgl првоерим не а дминке ли мы с API отладкой
#if UNITY_WEBGL && !UNITY_EDITOR
								WebGLDebug.Check(this.token);
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

					// если на сцене и есть position - значит куда то движтся. запишем куда
					if (player.id == id)
					{
						if (player.position != null)
							this.target = new Vector2(player.position[0], player.position[1]);

						// если мы движемся или остановились обнулим что мы не срабатывал тригер по долгому ожиданию ответа от сервера движения в методе CanMove
						if (player.action.IndexOf("idle") >= 0 || player.position != null)
						{
							if (player.action.IndexOf("idle") >= 0)
								this.moveTo = Vector2.zero;

							// если мы в движении запишем наш пинг (если только загрузились то оже пишется  - 0)
							// Todo сравнить что координаты изменены (может это не про движения данные пришли)
							if (pingTime == 0)
							{
								TimeSpan ts = DateTime.Now - this.lastMove;
								pingTime = Math.Round(ts.TotalSeconds, 4);
							}
						}
					}
				}
			}

			// если есть враги
			if (recive.enemys != null)
			{
				foreach (EnemyRecive enemy in recive.enemys)
				{
					string name = "enemy_" + enemy.id;
					GameObject prefab = GameObject.Find(name);
					if (prefab == null)
					{
						// данные от NPC что могут уже атаковать ДО загрузки сцены (те между sign и load)
						if (enemy.prefab == "") continue;

						Debug.Log("Создаем " + name);

						prefab = Instantiate(Resources.Load("Prefabs/Enemys/" + enemy.prefab, typeof(GameObject))) as GameObject;
						prefab.name = name;
						prefab.GetComponent<SpriteRenderer>().sortingOrder += (int)ground_sort;
						prefab.GetComponentInChildren<Canvas>().sortingOrder += (int)ground_sort + 1;
						prefab.transform.SetParent(world.transform, false);
					}

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
				foreach (ObjectRecive obj in recive.objects)
				{
					string name = "object_" + obj.id;
					GameObject prefab = GameObject.Find(name);
					if (prefab == null)
					{
						// данные от объектов что могут влиять на игру ДО загрузки сцены (те между sign и load)
						if (obj.prefab == "") continue;

						Debug.Log("Создаем " + name);

						prefab = Instantiate(Resources.Load("Prefabs/Objects/" + obj.prefab, typeof(GameObject))) as GameObject;
						prefab.name = name;
						prefab.GetComponent<SpriteRenderer>().sortingOrder += (int)ground_sort;
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

			if (recive.action == "load")
				pause = false;
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

		connect.error = null;
		connect.recives.Clear();

		if (exit)
		{
			Debug.LogWarning("уже закрываем игру ("+ error + ")");
			yield break;
		}

		exit = true;
		pause = true;
		connect.Close();

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
		if(connect!=null)
			connect.Close();
	}
}