using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Самоуничтожение объекта-эффекта появления (см. EntityModel.SpawnAppearFlash): ждёт, пока
	/// Universal Animator отыграет remove-стейт (Puff), и уничтожает свой GameObject. Без этого
	/// объект остался бы висеть с последним кадром эффекта — writeDefaults=false в Universal.controller
	/// держит последний кадр анимации после перехода в Idle.
	/// Страховка-таймаут — на случай, если триггер не сыграл и эффект-стейт так и не начался.
	/// </summary>
	internal class AppearFlashEffect : MonoBehaviour
	{
		private const float TIMEOUT_SEC = 3f;

		private Animator _animator;
		private float _spawnedAt;
		private bool _effectSeen;

		void Awake()
		{
			_animator = GetComponent<Animator>();
			_spawnedAt = Time.time;
		}

		void Update()
		{
			if (Time.time - _spawnedAt > TIMEOUT_SEC) { Destroy(gameObject); return; }
			if (_animator == null || _animator.runtimeAnimatorController == null) { Destroy(gameObject); return; }

			var state = _animator.GetCurrentAnimatorStateInfo(0);
			bool inIdle = state.IsName("Idle");
			if (!inIdle && !_effectSeen) _effectSeen = true;

			// Эффект отыгран: вернулись в Idle либо дошли до конца remove-стейта.
			if (_effectSeen && (inIdle || state.normalizedTime >= 1f))
				Destroy(gameObject);
		}
	}
}
