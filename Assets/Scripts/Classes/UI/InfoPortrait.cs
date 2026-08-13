using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Портрет в окне сведений: показывает выбранную сущность ЛИЦОМ к игроку и перебирает её действия по
	/// кругу — одно за другим, каждое целиком. Тем окно и отличается от рамки цели, где портрет повторяет
	/// то, что существо делает на карте прямо сейчас: здесь видно, на что оно способно вообще.
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
		private SpriterDotNetUnity.SpriterDotNetBehaviour _clipsOf;
		private int _clipIndex;

		/// <summary>
		/// Сколько секунд держится одно действие. Смена идёт по времени, а не по концу анимации: короткие
		/// (удар) мелькали бы, а показ должен успеть рассмотреться. Анимация внутри этого времени живёт
		/// сама — зацикленная повторяется, разовая (падение) замирает на последнем кадре и так и лежит.
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
			if (_mirrorSpriter == null || _mirrorSpriter.Animator == null)
				return;

			var mirror = _mirrorSpriter.Animator;

			if (_clips == null || _clipsOf != _mirrorSpriter)
			{
				_clips = CollectClips(mirror);
				_clipsOf = _mirrorSpriter;
				_clipIndex = 0;
				_clipStarted = Time.time;

				if (_clips.Count > 0)
					mirror.Play(_clips[0]);

				return;
			}

			if (_clips.Count < 2 || Time.time - _clipStarted < actionSeconds)
				return;

			_clipIndex = (_clipIndex + 1) % _clips.Count;
			_clipStarted = Time.time;
			mirror.Play(_clips[_clipIndex]);
		}

		/// <summary>
		/// Клипы действий сущности в ракурсе «лицом к игроку»: по действию из библиотеки берём тот же клип,
		/// что играл бы мир при взгляде вниз. Действие без клипа в этом ракурсе (нарисован только вид сзади)
		/// пропускаем — подставлять чужой ракурс значило бы показать существо спиной.
		/// </summary>
		private List<string> CollectClips(SpriterDotNetUnity.UnityAnimator mirror)
		{
			var clips = new List<string>();
			if (_target == null || string.IsNullOrEmpty(_target.prefab))
				return clips;

			foreach (string action in AnimationCacheService.GetPrefabActions(_target.prefab))
			{
				var resolved = AnimationCacheService.GetClipName(_target.prefab, action, FACING.x, FACING.y);
				if (string.IsNullOrEmpty(resolved.clipName) || clips.Contains(resolved.clipName))
					continue;
				if (!mirror.HasAnimation(resolved.clipName))
					continue;

				clips.Add(resolved.clipName);
			}

			// Библиотека действий пуста (не настроено в админке) — показываем то, что есть у самого скелета:
			// пустой портрет хуже показа со всеми ракурсами.
			if (clips.Count == 0)
				clips.AddRange(mirror.GetAnimations());

			return clips;
		}
	}
}
