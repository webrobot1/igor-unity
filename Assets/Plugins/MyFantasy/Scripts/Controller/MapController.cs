using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MyFantasy
{
	/// <summary>
	/// Класс для обработки ответов от сервера - карт
	/// </summary>
	public abstract class MapController : ConnectController 
	{
		/// <summary>
		/// объект в котором будут дочерние объекты карт
		/// </summary>
		[SerializeField]
		protected GameObject mapObject;

		/// <summary>
		/// родителький объект всех обектов
		/// </summary>
		[SerializeField]
		protected GameObject worldObject;

		/// <summary>
		/// массив с перечнем с какой стороны какая смежная карта
		/// </summary>
		protected Dictionary<int, string> sides = new Dictionary<int, string>();

		/// <summary>
		/// массив декодированных с сервера карт
		/// </summary>
		protected Dictionary<int, MapDecode> maps = new Dictionary<int, MapDecode>();

		protected virtual void Start()
		{
			if (mapObject == null)
				Error("не присвоен GameObject для карт");

			if (worldObject == null)
				Error("не присвоен GameObject для игровых обектов");
		}

		protected virtual IEnumerator GetMap(int map_id)
		{
			if (!sides.ContainsKey(map_id))
				Error("карта " + map_id + " не является какой либо частью текущих локаций");			
			else if (mapObject.transform.Find(sides[map_id]) != null)
				Error("карта " + map_id + " уже выгружена в игровое пространство");
			else if (maps.ContainsKey(map_id))
				Error("попытка загрузки карты " + map_id + " повторно");
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


				// todo может сделать какую то авторизацию для получения карт
				WWWForm formData = new WWWForm();
				formData.AddField("map_id", map_id);
				formData.AddField("token", player_token);

				string url = "http://" + SERVER + "/game/signin/get_map/?map_id="+ map_id + "&token="+ player_token;
				Debug.Log("получаем карту " + map_id + " с " + url);

				UnityWebRequest request = UnityWebRequest.Post(url, formData);

				yield return request.SendWebRequest();

				// проверим что пришло в ответ
				string text = request.downloadHandler.text;
				if (text.Length > 0)
				{
					#if UNITY_EDITOR
						Debug.Log("Ответ от сервера карт " + text);
					#endif
					try
					{
						MapDecodeRecive recive = JsonConvert.DeserializeObject<MapDecodeRecive>(text);

						if (recive.error.Length > 0)
						{
							Error("Ошибка запроса карты " + map_id + ": " + recive.error);
						}
						else if (recive.map.Length > 0)
						{
							Debug.Log("Обновляем карту " + map_id);

							Transform grid = new GameObject(sides[map_id]).transform;
							grid.gameObject.AddComponent<Grid>();
							grid.SetParent(mapObject.transform, false);

							// приведем координаты в сответсвие с сеткой Unity
							try
							{
								maps.Add(map_id, MapDecodeModel.generate(recive.map, grid));
								SortMap();
							}
							catch (Exception ex)
							{
								Error("Ошибка разбора карты", ex);
							}
						}
						else
							Error("ответ запроса сервера карт не содержит карту");
					}
					catch (Exception ex)
					{
						Error("Ошибка запроса карты", ex);
					}
				}
				else
					Error("Пустой ответ сервера карт  " + request.error);
			}

			yield break;
		}

		protected void SortMap()
		{
			foreach (Transform grid in mapObject.transform)
			{
				int center = sides.FirstOrDefault(x => x.Value == "center").Key;
				switch (grid.gameObject.name)
				{
					case "center":

						// если у нас webgl првоерим не а дминке ли мы с API отладкой
						#if UNITY_WEBGL && !UNITY_EDITOR
							WebGLSupport.WebGLDebug.DebugCheck(center);
						#endif

						grid.localPosition = new Vector3(0f, 0f, 0f);
						break;

					case "right":
						// центральная карта может отсутвуовать например когда мы ушли с одной карты на другую и переиспользовали графику (SortMap  запустится но оттуда откуда пришли могло не быть графики карты куда пришли)
						if (maps.ContainsKey(center))
							grid.localPosition = new Vector3(maps[center].width, 0, 0);
							
						break;

					case "left":
						grid.localPosition = new Vector3(maps[sides.FirstOrDefault(x => x.Value == grid.gameObject.name).Key].width * -1, 0, 0);
					break;
				}

				// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
				if (worldObject.transform.Find(grid.gameObject.name) != null)
				{
					worldObject.transform.Find(grid.gameObject.name).localPosition = grid.localPosition;

					int map_id = sides.FirstOrDefault(x => x.Value == grid.gameObject.name).Key;
					foreach (Transform child in worldObject.transform.Find(grid.gameObject.name))
					{
						var model = child.GetComponent<ObjectModel>();
						if (model != null)
						{
							if (child.gameObject.GetComponent<SpriteRenderer>())
								child.gameObject.GetComponent<SpriteRenderer>().sortingOrder = maps[map_id].spawn_sort + model.sort;

							if (child.gameObject.GetComponentInChildren<Canvas>())
								child.gameObject.GetComponentInChildren<Canvas>().sortingOrder = maps[map_id].spawn_sort + 1 + model.sort;
						}
					}
				}
			}
		}
	}
}