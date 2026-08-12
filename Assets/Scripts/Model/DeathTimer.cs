using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Полоска над мёртвым телом: сколько ему осталось до исхода. Исход и срок зависят от того, чьё тело:
	/// игрок по истечении поднимается на месте, прочие существа уходят с карты и возвращаются на точку
	/// появления. Сроки ведут РАЗНЫЕ группы событий, и совпадать они не обязаны — потому каждая сторона
	/// читает СВОЮ группу, а не общую.
	///
	/// Полная длина полоски — длительность самого срока: сервер шлёт её всем видящим тело рядом с
	/// остатком, потому подошедший позже видит ту же полоску, что и стоявший рядом с самого начала.
	///
	/// Компонент навешивает сама сущность при входе в смерть (ObjectModel) — по действию, а не по составу
	/// компонентов: тело без добычи тоже имеет срок.
	///
	/// Идёт ли срок, решает ПУСТОЙ ЗАПАС ЗДОРОВЬЯ — тем же условием сервер этот срок и заводит. Ни
	/// действие сущности, ни остаток события признаком не годятся: тело двигают, и по действию отсчёт
	/// пропадал бы у идущего срока, а остаток сервер после срабатывания отсчитывает заново — у сроков,
	/// которые уже вышли, он такой же ненулевой, как у идущих.
	/// </summary>
	public class DeathTimer : MonoBehaviour
	{
		// Группы срока: у игрока — до подъёма на месте, у прочих — до ухода тела с карты.
		public const string GROUP_PLAYER = "status/resurrect";
		public const string GROUP_ENTITY = "status/despawn";

		// Первая полоска под телом: срок относится к самому телу, потому он ближе к нему, а очередь на
		// добычу укладывается следующей ниже (WorldBar.PlaceUnder).
		private const int Order = 71;

		// Уход тела — красным, воскрешение — синим: первое отсчитывает потерю (тело и невынесенная
		// добыча пропадут), второе ожидание возврата в игру.
		private static readonly Color DespawnColor = new Color(0.85f, 0.25f, 0.25f, 1f);
		private static readonly Color ResurrectColor = new Color(0.45f, 0.7f, 1f, 1f);

		// Запас здоровья объявлен у EnemyModel: срок лежания есть только у того, кто умирает. Прочие
		// носители ObjectModel (сундук) в смерть не входят, и компонент им не навешивается.
		private EnemyModel _model;
		private WorldBar _bar;

		private void Awake()
		{
			_model = GetComponent<EnemyModel>();
		}

		private void LateUpdate()
		{
			if (_model == null || _model.hp != 0)
			{
				Hide();
				return;
			}

			// Группа — по ВИДУ тела, не по тому, моё оно или чужое: у чужого игрока исход тот же, что у
			// своего, и срок ему ведёт та же группа. По владельцу над чужим телом спрашивался бы срок
			// ухода, которого у игрока нет вовсе, — отсчёт до его подъёма пропадал бы у всех, кроме него.
			bool isPlayer = _model is PlayerModel;
			string group = isPlayer ? GROUP_PLAYER : GROUP_ENTITY;

			double remain = _model.GetEventRemain(group);
			if (remain <= 0)
			{
				Hide();
				return;
			}

			// Полная длина шкалы — длительность самого срока. Ещё не пришла (первый кадр после смерти) —
			// считаем от текущего остатка, полоска стартует полной.
			double? length = _model.getEvent(group).timeout;
			double full = length.HasValue && length.Value > 0 ? length.Value : remain;

			if (_bar == null)
				_bar = WorldBar.Create(transform, "DeathBar", Order);

			if (_bar == null)
				return;

			_bar.Show(
				WorldBar.PlaceUnder(_model, transform, 0f),
				(float)(remain / full),
				isPlayer ? ResurrectColor : DespawnColor,
				null,
				GameIcons.Skull
			);
		}

		private void Hide()
		{
			if (_bar != null) _bar.Hide();
		}

		private void OnDestroy()
		{
			if (_bar != null) Destroy(_bar.gameObject);
		}
	}
}
