using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Класс для обработки ответов от сервера - карт
	/// </summary>
	public abstract class MapController : ConnectController 
	{
		[Header("Укажите родительский GameObject карт и существ")]

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
		private static Dictionary<int, Point> _sides = new Dictionary<int, Point>();

		/// <summary>
		/// массив декодированных с сервера карт
		/// </summary>
		private static Dictionary<int, MapDecode> _maps = new Dictionary<int, MapDecode>();

		protected override void Awake()
		{
			base.Awake();

			if (mapObject == null)
				Error("Карты: не присвоен GameObject для карт");

			if (worldObject == null)
				Error("Карты: не присвоен GameObject для игровых обектов");


			// на случай если мы как разработчик какие то тестовые данные оставили
			foreach (Transform transform in mapObject.transform)
			{
				DestroyImmediate(transform.gameObject);
			}

			foreach (Transform transform in worldObject.transform)
			{
				DestroyImmediate(transform.gameObject);
			}

			// определяем здесь что бы сбросить статичные свойства если мы перезаходили в игру
			// сбрасываем тк при разработке некие опции у нас стоят что не очищают при отладке эти данные https://youtu.be/sRx14YMbLuw
			_sides.Clear();
			_maps.Clear();	
		}

		/// <summary>
		/// Обработка пакета - с какой стороны какая ID карты на сцене
		/// </summary>
		protected virtual void HandleData<P, E, O>(Recive<P, E, O> recive) where P : EntityRecive where E : EntityRecive where O : EntityRecive
		{
			if (recive.sides != null)
			{
				Debug.Log("Карты: Обрабатываем стороны карт");

				// если уже есть загруженные карты (возможно мы перешли на другую локацию бесшовного мира) попробуем переиспользовать их (скорее всего мы перешли на другую карту где схожие смежные карты могут быть)
				if (_maps.Count > 0)
				{
					foreach (Transform grid in mapObject.transform)
					{
						int map_id = Int32.Parse(grid.name);
						if (!recive.sides.ContainsKey(map_id))
						{
							Debug.Log("Карты: уничтожаем неиспользуемую карту " + map_id);
							DestroyImmediate(mapObject.transform.Find(map_id.ToString()).gameObject);
							DestroyImmediate(worldObject.transform.Find(map_id.ToString()).gameObject);

							_maps.Remove(map_id);
						}
					}
				}

				MapController._sides = recive.sides;
				SortMap();

				// загрузим отвутвующую графику центральной и смежных карт 
				// TODO сделать загрузку смежных карт если мы рядок к их краю и удалять графику если далеко (думаю это в CameraController можно сделать) в Update (и помечать что мы уже загружаем карту в корутине)
				foreach (KeyValuePair<int, Point> side in recive.sides)
				{
					if (!_maps.ContainsKey(side.Key))
					{
						StartCoroutine(Patcher.GetMap(_sides[side.Key].ip, GAME_ID, player_token, side.Key, _sides[side.Key].updated, (Patcher patcher) =>
						{
							if (patcher.error != null)
								Error("Карты: ошибка " + patcher.error);
							if (patcher.result == null || patcher.result.Length == 0)
								Error("Карты: пришел пустой ответ от патчера");
							else
							{
								Debug.Log("Карты: Обновляем " + side.Key);

								// приведем координаты в сответсвие с сеткой Unity
								try
								{
									if (!_sides.ContainsKey(side.Key))
										Debug.LogError("Карты: " + side.Key + " загружена в то время когда уже ее нет в массиве сторон (возможно игрок уже ушел с карты где она была нужна)");
									else if (mapObject.transform.Find(side.Key.ToString()) != null)
										Error("Карты: " + side.Key + " уже выгружена в игровое пространство");
									else if (_maps.ContainsKey(side.Key))
										Error("Карты: попытка загрузки " + side.Key + " повторно");
									else
									{
										Transform grid = new GameObject(side.Key.ToString()).transform;
										grid.gameObject.AddComponent<Grid>();
										grid.SetParent(mapObject.transform, false);

										_maps.Add(side.Key, MapDecodeModel.generate(patcher.result, grid));
										SortMap();
									}
								}
								catch (Exception ex)
								{
									Error("Карты: Ошибка разбора карты", ex);
								}
							}
						}));
					}
				}
			}
		}

		public static Dictionary<int, MapDecode> getMaps()
        {
			return _maps;
		}		
		
		public static Dictionary<int, Point> getSides()
        {
			return _sides;
		}
		
		private void SortMap()
		{
			foreach (Transform grid in mapObject.transform)
			{
				int map_id = Int32.Parse(grid.name);
				if (_sides.ContainsKey(map_id))
				{
					grid.localPosition = new Vector2(_sides[map_id].x, _sides[map_id].y);

					// мы сортировку устанавливаем в двух местах - здесь и при приходе данных сущностей. тк объекты могут быть загружены раньше карты и наоборот
					if (worldObject.transform.Find(grid.gameObject.name) != null)
					{
						worldObject.transform.Find(grid.gameObject.name).localPosition = grid.localPosition;
						foreach (Transform child in worldObject.transform.Find(grid.gameObject.name))
						{
							var model = child.GetComponent<EntityModel>();
							if (model != null)
							{
								if (child.gameObject.GetComponent<SpriteRenderer>())
									child.gameObject.GetComponent<SpriteRenderer>().sortingOrder = _maps[map_id].spawn_sort + model.sort;

								if (child.gameObject.GetComponentInChildren<Canvas>())
									child.gameObject.GetComponentInChildren<Canvas>().sortingOrder = _maps[map_id].spawn_sort + 1 + model.sort;
							}
						}
					}
				}
				else
					Error("Карты: На сцене присутвует карта "+ map_id + " которая не является текущей или смежной");
			}
		}
	}
}