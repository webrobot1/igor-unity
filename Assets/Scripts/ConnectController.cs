using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using WebGLSupport;

/// <summary>
/// Класс для обработки запросов, конект
/// </summary>
public abstract class ConnectController :Controller
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
	/// индентификатор игрока в бд, для индентификации нашего игрока среди всех на карте
	/// </summary>
	private int? id = null;

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
	/// время последнего шага нашего игрока
	/// </summary>
	protected DateTime lastMove = DateTime.Now;

	/// <summary>
	/// Позиция к которой движется наш персонаж (пришла от сервера)
	/// </summary>
	protected Vector2 target;	
	
	/// <summary>
	/// Позиция к которой движется наш персонаж (пришла от сервера)
	/// </summary>
	[SerializeField]
	private Cinemachine.CinemachineVirtualCamera camera;	
	
	/// <summary>
	/// тайловая сетка карты
	/// </summary>
	[SerializeField]
	private GameObject grid;

	/// <summary>
	/// на каком уровне слоя размещать новых персонажей и npc 
	/// </summary>
	private int? ground_sort = null;

	/// <summary>
	/// Проверка наличие новых данных или ошибок соединения
	/// </summary>
	protected void Update()
	{
		if (connect != null && !exit)
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
		else if(this.id == null)
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
		this.id = data.id;
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
			if (recive.map != null)
			{
				// удалим  все слои что были ранее
				for (int i = 0; i < grid.transform.parent.childCount; i++)
				{
					if(grid.transform.parent.GetChild(i).gameObject.GetInstanceID()!=grid.GetInstanceID())
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

								if (map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites !=null)
								{
									newTile.sprites = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprites;
								}
								else
									newTile.sprite = map.tileset[tile.Value.tileset_id].tile[tile.Value.tile_id].sprite;
								
								tilemap.SetTile(new Vector3Int(tile.Value.x, tile.Value.y, 0), newTile);
							}
						}
						Debug.LogError(newLayer.name + " раставлены tile");		
					}
					else if (layer.objects !=null)
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
						if (sort == 1)
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
					GameObject prefab = GameObject.Find("player_" + player.id);

					// если игрока нет на сцене
					if (prefab == null)
					{

						// если это не полная загрузка мира и игрок не добавляется на карту и при этом нет такого игркоа на карте - это запоздавшие сообщение разлогиненного
						if (recive.action != "load" && player.action != "online") continue;

						prefab = Instantiate(Resources.Load("Prefabs/" + player.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "player_" + player.id;
						prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)ground_sort;
					
						if (player.id == this.id)
						{
							//transform.SetParent(prefab.transform);
							//transform.position = new Vector3(transform.parent.position.x, transform.parent.position.y, transform.position.z);
							camera.Follow = prefab.transform;
							this.player = prefab.GetComponent<PlayerModel>();

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
						StartCoroutine(LoadRegister("Не удалось загрузить игрока:" + ex));
                    }

					// если на сцене и есть position - значит куда то движтся. запишем куда
					if (player.position != null)
					{	
						if (player.id == this.id) { 
							this.target = new Vector2(player.position[0], player.position[1]);

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
					GameObject prefab = GameObject.Find("enemy_" + enemy.id);
					if (prefab == null)
					{
						prefab = Instantiate(Resources.Load("Prefabs/" + enemy.prefab, typeof(GameObject))) as GameObject;
						prefab.name = "enemy_" + enemy.id;
						prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)ground_sort;
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
						prefab.GetComponent<SpriteRenderer>().sortingOrder = (int)ground_sort;
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

#if !UNITY_WEBGL
	public void OnApplicationPause(bool pause)
	{
		StartCoroutine(LoadRegister("Вы вышли из игры"));
	}
#endif

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

		exit = true;

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

		connect.Close();
		SceneManager.UnloadScene("MainScene");
		Camera.main.GetComponent<RegisterController>().Error(error);
	}

	void OnApplicationQuit()
	{
		if(connect!=null)
			connect.Close();
	}
}