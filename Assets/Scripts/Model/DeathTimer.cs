using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Полоска над мёртвым телом: сколько ему осталось до исхода. Исход и срок зависят от того, чьё тело:
	/// игрок по истечении поднимается на месте, прочие существа уходят с карты и возвращаются на точку
	/// появления. Сроки ведут РАЗНЫЕ группы событий, и совпадать они не обязаны — потому каждая сторона
	/// читает СВОЮ группу, а не общую.
	///
	/// Полная длина полоски — САМЫЙ БОЛЬШОЙ остаток, который эта сущность показала: длительность срока
	/// сервер сообщает только владельцу события, а над чужим телом её взять неоткуда. Подошедший к телу
	/// позже видит полоску от своего окна наблюдения — «сколько ещё есть время» она показывает верно,
	/// сравнивать по ней два разных тела нельзя.
	///
	/// Компонент навешивает сама сущность при входе в смерть (ObjectModel) — по действию, а не по составу
	/// компонентов: тело без добычи тоже имеет срок.
	/// </summary>
	public class DeathTimer : MonoBehaviour
	{
		// Группы срока: у игрока — до подъёма на месте, у прочих — до ухода тела с карты.
		public const string GROUP_PLAYER = "status/resurrect";
		public const string GROUP_ENTITY = "status/despawn";

		// Под ногами тела, с запасом от спрайта: сущность привязана ногами, и полоска у самого нуля легла бы
		// поверх лежащего тела. Очередь на добычу висит НАД телом — так две полоски не спорят за место.
		private const float WorldOffsetY = -0.2f;
		private const int Order = 71;

		// Уход тела — красным, своё воскрешение — синим: первое отсчитывает потерю (тело и невынесенная
		// добыча пропадут), второе ожидание возврата в игру.
		private static readonly Color DespawnColor = new Color(0.85f, 0.25f, 0.25f, 1f);
		private static readonly Color ResurrectColor = new Color(0.45f, 0.7f, 1f, 1f);

		private EntityModel _model;
		private WorldBar _bar;

		private string _group;
		private double _full;

		private void Awake()
		{
			_model = GetComponent<EntityModel>();
		}

		private void LateUpdate()
		{
			if (_model == null || _model.action != "dead")
			{
				Reset();
				return;
			}

			bool mine = PlayerController.Player != null && _model.key == PlayerController.Player.key;
			string group = mine ? GROUP_PLAYER : GROUP_ENTITY;

			// Смена стороны отсчёта (своё тело ↔ чужое) обнуляет запомненную длину: сроки у групп разные.
			if (group != _group)
			{
				_group = group;
				_full = 0;
			}

			double remain = _model.GetEventRemain(group);
			if (remain <= 0)
			{
				Hide();
				return;
			}

			if (remain > _full)
				_full = remain;

			if (_bar == null)
				_bar = WorldBar.Create(transform, "DeathBar", Order);

			if (_bar == null)
				return;

			_bar.Show(
				transform.position + new Vector3(0f, WorldOffsetY, 0f),
				(float)(remain / _full),
				mine ? ResurrectColor : DespawnColor,
				null,
				GameIcons.Skull
			);
		}

		private void Hide()
		{
			if (_bar != null) _bar.Hide();
		}

		private void Reset()
		{
			_full = 0;
			Hide();
		}

		private void OnDestroy()
		{
			if (_bar != null) Destroy(_bar.gameObject);
		}
	}
}
