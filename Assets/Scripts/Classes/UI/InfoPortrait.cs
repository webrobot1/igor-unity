using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Портрет в окне сведений: показывает выбранную сущность ЛИЦОМ к игроку и перебирает её действия по
	/// кругу — одно за другим, каждое целиком. Тем окно и отличается от рамки цели, где портрет повторяет
	/// то, что существо делает на карте прямо сейчас: здесь видно, на что оно способно вообще.
	/// Клип действия идёт ПО КРУГУ всё время своего показа, объявлено действие однократным либо нет:
	/// однократность принадлежит бою на карте, а витрине показывать нечего, если удар мелькнёт один раз.
	/// Ракурс всегда один (взгляд вниз, на игрока): показ вертится не ради обзора модели, а ради действий,
	/// и смена ракурсов мешала бы их различать. У неанимированной цели перебирать нечего — виден её кадр.
	/// </summary>
	public class InfoPortrait : EntityPortrait
	{
		// Взгляд сущности на игрока: вниз экрана. Тем же направлением клипы резолвит игровой мир, поэтому
		// ракурс берётся штатным подбором, а не разбором имён анимаций.
		private static readonly Vector2 FACING = new Vector2(0, -1);

		// Клипы действий в выбранном ракурсе и место в переборе. Список снимается с зеркала, поэтому
		// пересобирается вместе с ним (смена цели, поздняя загрузка анимации).
		private List<string> _clips;
		private Spine.Unity.SkeletonAnimation _clipsOf;
		private int _clipIndex;

		/// <summary>
		/// Сколько секунд держится одно действие. Смена идёт по времени, а не по концу анимации: короткие
		/// (удар) мелькали бы, а показ должен успеть рассмотреться. Внутри этого времени клип идёт по
		/// кругу — за четыре секунды удар длиной в треть секунды повторится десяток раз.
		/// </summary>
		[SerializeField]
		private float actionSeconds = 4f;

		// Когда началось текущее действие.
		private float _clipStarted;

		public ObjectModel Target
		{
			get
			{
				return _target;
			}
			set
			{
				if (_target == value)
					return;

				_target = value;
				ApplyVisual(value);
				_clips = null;
				_clipsOf = null;
				_clipIndex = 0;
			}
		}

		private void FixedUpdate()
		{
			PortraitUpdate();
		}

		protected override void SyncMirror()
		{
			if (!MirrorReady)
				return;

			if (_clips == null || _clipsOf != Mirror)
			{
				_clips = CollectClips();
				_clipsOf = Mirror;
				_clipIndex = 0;
				_clipStarted = Time.time;

				if (_clips.Count > 0)
					MirrorPlay(_clips[0]);

				return;
			}

			if (_clips.Count < 2 || Time.time - _clipStarted < actionSeconds)
				return;

			_clipIndex = (_clipIndex + 1) % _clips.Count;
			_clipStarted = Time.time;
			MirrorPlay(_clips[_clipIndex]);
		}

		/// <summary>
		/// Клипы действий сущности в ракурсе «лицом к игроку»: по действию из библиотеки берём тот же клип,
		/// что играл бы мир при взгляде вниз. Действие без клипа в этом ракурсе (нарисован только вид сзади)
		/// пропускаем — подставлять чужой ракурс значило бы показать существо спиной.
		/// </summary>
		private List<string> CollectClips()
		{
			var clips = new List<string>();
			if (_target == null || string.IsNullOrEmpty(_target.prefab))
				return clips;

			foreach (string action in AnimationCacheService.GetPrefabActions(_target.prefab))
			{
				var resolved = AnimationCacheService.GetClipName(_target.prefab, action, FACING.x, FACING.y);
				if (string.IsNullOrEmpty(resolved.clipName) || clips.Contains(resolved.clipName))
					continue;
				if (!MirrorHasClip(resolved.clipName))
					continue;

				clips.Add(resolved.clipName);
			}

			// Библиотека действий пуста (не настроено в админке) — показываем то, что есть у самого тела:
			// пустой портрет хуже показа со всеми ракурсами.
			if (clips.Count == 0)
				clips.AddRange(MirrorClips());

			return clips;
		}
	}
}
