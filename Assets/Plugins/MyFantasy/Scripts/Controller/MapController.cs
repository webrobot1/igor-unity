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
		protected Dictionary<int, Point> sides = new Dictionary<int, Point>();

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
		
		public Dictionary<int, MapDecode> getMaps()
        {
			return maps;
		}		
		
		public Dictionary<int, Point> getSides()
        {
			return sides;
		}

		protected virtual IEnumerator DownloadMap(int map_id)
		{
			if (!sides.ContainsKey(map_id))
				Error("карта " + map_id + " не является какой либо частью текущих локаций");			
			else if (mapObject.transform.Find(map_id.ToString()) != null)
				Error("карта " + map_id + " уже выгружена в игровое пространство");
			else if (maps.ContainsKey(map_id))
				Error("попытка загрузки карты " + map_id + " повторно");
			else
			{
				string url = "http://" + SERVER + "/game/signin/get_map/?game_id="+ GAME_ID + "&map_id="+ map_id + "&token="+ player_token;
				Debug.Log("получаем карту " + map_id + " с " + url);

				UnityWebRequest request = UnityWebRequest.Get(url);

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

							Transform grid = new GameObject(map_id.ToString()).transform;
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

				request.Dispose();
			}			

			yield break;
		}

		protected void SortMap()
		{
			foreach (Transform grid in mapObject.transform)
			{
				int map_id = Int32.Parse(grid.name);
				grid.localPosition = new Vector2(sides[map_id].x, sides[map_id].y);

				// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
				if (worldObject.transform.Find(grid.gameObject.name) != null)
				{
					worldObject.transform.Find(grid.gameObject.name).localPosition = grid.localPosition;
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