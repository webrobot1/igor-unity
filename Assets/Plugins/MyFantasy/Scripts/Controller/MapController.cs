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
		protected Dictionary<string, int> sides = new Dictionary<string, int>();

		/// <summary>
		/// массив декодированных с сервера карт
		/// </summary>
		protected Dictionary<string, MapDecode> maps = new Dictionary<string, MapDecode>();

		protected  void Awake()
		{
			if (mapObject == null)
				Error("не присвоен GameObject для карт");

			if (worldObject == null)
				Error("не присвоен GameObject для игровых обектов");
		}

		protected virtual IEnumerator GetMap(string side)
		{
			if (mapObject.transform.Find(side) != null)
				Error("карта " + side + " уже выгружена в игровое пространство");
			else if (maps.ContainsKey(side))
				Error("попытка загрузки карты " + side + " повторно");
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
				formData.AddField("token", this.player_token);
				formData.AddField("side", side);

				string url = "http://" + SERVER + "/server/signin/get_map";
				Debug.Log("получаем карту " + side + " с " + url);

				UnityWebRequest request = UnityWebRequest.Post(url, formData);

				yield return request.SendWebRequest();

				// проверим что пришло в ответ
				string text = request.downloadHandler.text;
				if (text.Length > 0)
				{
					Debug.Log("Ответ от сервера карт " + text);

					try
					{
						MapDecodeRecive recive = JsonConvert.DeserializeObject<MapDecodeRecive>(text);

						if (recive.error.Length > 0)
						{
							Error("Ошибка запроса карты " + side + ": " + recive.error);
						}
						else if (recive.map.Length > 0)
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
						grid.localPosition = new Vector3(maps[side].width * -1, 0, 0);
						break;
				}

				// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
				if (worldObject.transform.Find(side) != null)
				{
					foreach (Transform child in worldObject.transform.Find(side))
					{
						var model = child.GetComponent<ObjectModel>();
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
	}
}