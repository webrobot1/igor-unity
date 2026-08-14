using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Живой портрет сущности в интерфейсе: показывает выбранную сущность отдельной камерой, зеркаля её
	/// Spriter-анимацию (у неанимированных — спрайт). Носителей два и они видны одновременно — рамка цели
	/// и окно информации, — поэтому портрет заведён отдельным классом, а не остался частью рамки.
	///
	/// Ожидаемая раскладка объектов: камера — РОДИТЕЛЬ этого объекта, сам объект несёт SpriteRenderer
	/// (кадр неанимированных) и Animator. Наследник решает, ЧТО показывать (кого выбрали) и по какому
	/// правилу играть анимацию зеркала (<see cref="SyncMirror"/>).
	/// </summary>
	public abstract class EntityPortrait : MonoBehaviour
	{
		/// <summary>
		/// Пропорция отдаления камеры для НЕанимированных целей: множитель мирового размера спрайта.
		/// У Spriter-целей отдаление считается по фактическим границам зеркала (см. CameraUpdate).
		/// </summary>
		[SerializeField]
		protected float aspect = 30;

		/// <summary>
		/// Насколько кадр шире показанной сущности. 1 — кадр ровно по её границам, больше — с полями вокруг,
		/// меньше — приближение вплотную. Приближают потому, что границы считаются по частям скелета, а те
		/// нарисованы с запасом прозрачного поля: по ним фигура выходит заметно мельче, чем видит игрок.
		/// </summary>
		[SerializeField]
		protected float frameMargin = 1.111f;

		/// <summary>
		/// Держать кадр неподвижным после первого замера. Габариты фигуры считаются по её текущей позе, а
		/// замах оружием их расширяет — пересчёт на каждом кадре отодвигал бы камеру, и тело на глазах
		/// мельчало бы при каждом ударе. Витрине действий (окно сведений) это мешает, живой рамке цели —
		/// нет: там показано то, что происходит на карте, и кадр обязан следовать за целью.
		/// </summary>
		[SerializeField]
		protected bool lockFrame = false;

		// Замер кадра, зафиксированный при lockFrame: угол обзора и положение фигуры. Сбрасывается вместе
		// с визуалом — у новой цели свои габариты.
		private bool _frameLocked;
		private float _lockedFov;
		private Vector3 _lockedPosition;

		// Сколько кадров подряд габариты фигуры держатся неизменными. Зеркало собирается не мгновенно:
		// части встают по местам и получают масштаб источника в течение нескольких кадров, и всё это
		// время фигура заметно крупнее итоговой. Замер по ней запирал кадр вокруг раздутых границ —
		// тело выходило мелким и смещённым, а спасало бы только переоткрытие окна. Поэтому считаем не
		// кадры от начала, а кадры БЕЗ изменений, и сбрасываем счёт, едва габариты поехали.
		private int _measured;
		private const int MEASURE_FRAMES = 5;

		// Насколько габариты вправе дрогнуть, оставаясь «теми же»: доля от высоты фигуры. Поза дышит
		// даже в покое, и требовать полного совпадения значило бы не закрепить кадр никогда.
		private const float MEASURE_TOLERANCE = 0.02f;

		// Высота фигуры на прошлом кадре — с ней и сверяемся. 0 — ещё не мерили.
		private float _measuredHeight;

		protected Animator animator;
		protected SpriteRenderer spriteRender;
		protected Camera face_camera;

		// Spriter-behaviour'ы исходный (на сущности) и зеркало (на этом UI-GO). Источник нужен, чтобы
		// зеркало не жило своей жизнью: без синхронизации оно вечно играет первую анимацию SCML.
		protected SpriterDotNetUnity.SpriterDotNetBehaviour _sourceSpriter;
		protected SpriterDotNetUnity.SpriterDotNetBehaviour _mirrorSpriter;

		protected ObjectModel _target = null;

		protected virtual void Awake()
		{
			animator = GetComponent<Animator>();
			face_camera = transform.parent.GetComponent<Camera>();
			spriteRender = GetComponent<SpriteRenderer>();

			if (animator == null)
				PlayerController.Error("не наден компонент аниматор в портрете сущности " + name);

			if (face_camera == null)
				PlayerController.Error("у портрета сущности " + name + " родительский объект не несёт камеру");
		}

		/// <summary>
		/// Показать сущность: собрать зеркало её Spriter-анимации либо (у неанимированных) скопировать спрайт.
		/// Прежнее зеркало снимается здесь же — метод зовут и при смене цели, и при позднем появлении Spriter.
		/// </summary>
		protected void ApplyVisual(ObjectModel value)
		{
			ClearVisual();

			if (value == null)
				return;

			var localSr = spriteRender;
			var srcSpriter = value.GetComponent<SpriterDotNetUnity.SpriterDotNetBehaviour>();

			// Spriter имеет приоритет над Animator: у Spriter-сущностей параллельно живёт Universal Animator
			// (overlay-эффекты remove/dead) и его animator.enabled==true. Правило «Spriter ⇒ animator выключен»
			// неверно — по нему портрет уходил в Animator-ветку с пустой Idle-state и оставался чёрным.
			if (srcSpriter != null && srcSpriter.SpriterData != null)
			{
				animator.runtimeAnimatorController = null;

				// Зеркалим Spriter-анимацию, чтобы камера снимала её вживую. SpriteRenderer оставляем с
				// корневым fallback-спрайтом (его границы нужны CameraUpdate), но рендер выключаем —
				// показывают Spriter-дети.
				SpriteRenderer srcFallbackSr = value.GetComponentInChildren<SpriteRenderer>(true);
				if (localSr != null)
				{
					if (srcFallbackSr != null && srcFallbackSr.sprite != null)
						localSr.sprite = srcFallbackSr.sprite;
					localSr.enabled = false;
				}

				_mirrorSpriter = NewSpriterRuntimeImporter.MirrorFromSource(srcSpriter, gameObject);
				_sourceSpriter = srcSpriter;
			}
			else
			{
				// Статичный фолбэк для не-анимированных целей.
				animator.runtimeAnimatorController = null;
				if (localSr != null) localSr.enabled = true;
				SpriteRenderer srcSr = value.GetComponentInChildren<SpriteRenderer>(true);
				if (srcSr == null)
					PlayerController.Error("На выбранном объекте налюдения присутвует колайдер но отсутвует Animator и SpriteRenderer");
				if (localSr != null)
					localSr.sprite = srcSr != null ? srcSr.sprite : null;
			}
		}

		/// <summary>
		/// Снять ранее собранное зеркало: без этого следующая цель показывалась бы поверх прежней.
		/// </summary>
		protected void ClearVisual()
		{
			NewSpriterRuntimeImporter.ClearMirror(gameObject);
			_sourceSpriter = null;
			_mirrorSpriter = null;
			_frameLocked = false;
			_measured = 0;
			_measuredHeight = 0;
		}

		/// <summary>
		/// Пер-кадровая работа портрета: поздняя привязка Spriter, обновление кадра статичной цели,
		/// правило анимации зеркала и наводка камеры. Наследник зовёт это, пока цель показана.
		/// </summary>
		protected void PortraitUpdate()
		{
			if (_target == null)
				return;

			// Spriter мог привязаться к цели уже ПОСЛЕ выбора (SCML грузится асинхронно): при выборе его
			// не было, ушли в ветку статичного кадра. Ловим появление и пересобираем зеркало.
			var srcSpriter = _target.GetComponent<SpriterDotNetUnity.SpriterDotNetBehaviour>();
			if (srcSpriter != null && srcSpriter.SpriterData != null && transform.Find("Sprites") == null)
			{
				ApplyVisual(_target);
				return;
			}

			// Статичная цель (kind-only/image, без зеркала и без legacy controller'а): спрайт источника
			// меняется в рантайме — Universal dead/remove перезаписывает его (placeholder → dead-кадр), а
			// restore возвращает обратно. Без пер-кадровой синхронизации портрет показывает устаревший
			// снимок (unknow при лежащем dead-черепе).
			if (animator.runtimeAnimatorController == null && transform.Find("Sprites") == null
				&& spriteRender != null)
			{
				var srcSr = _target.GetComponentInChildren<SpriteRenderer>(true);
				if (srcSr != null && srcSr.sprite != null && spriteRender.sprite != srcSr.sprite)
					spriteRender.sprite = srcSr.sprite;
			}

			SyncMirror();
			CameraUpdate();
		}

		/// <summary>
		/// Чем занято зеркало в этом кадре. По умолчанию повторяет анимацию источника — портрет показывает
		/// то же, что сущность делает на карте. Наследник вправе играть своё (окно информации перебирает
		/// действия по кругу, показывая, на что существо вообще способно).
		/// </summary>
		protected virtual void SyncMirror()
		{
			if (_sourceSpriter == null || _sourceSpriter.Animator == null
				|| _mirrorSpriter == null || _mirrorSpriter.Animator == null
				|| _sourceSpriter.Animator.CurrentAnimation == null)
				return;

			string srcName = _sourceSpriter.Animator.CurrentAnimation.Name;
			if (_mirrorSpriter.Animator.CurrentAnimation == null
				|| _mirrorSpriter.Animator.CurrentAnimation.Name != srcName)
			{
				if (_mirrorSpriter.Animator.HasAnimation(srcName))
					_mirrorSpriter.Animator.Play(srcName);
			}
		}

		// если изображения анимации с сильно отличабщимеся pivot to возможно надо будет каждый FixedUpdate делать этот метод для пересчета положения камеры и объекта что бы он не выходил за рамки
		protected void CameraUpdate()
		{
			// Spriter-цель: есть child "Sprites" с N body-part'ами mirror-анимации.
			// Центрируем портрет по world-AABB mirror-спрайтов (иначе pivot fallback-спрайта смещает
			// камеру и видны только ноги) и ставим fov по честной перспективной формуле, учитывающей
			// фактическое расстояние от камеры до контента: fov_v = 2*atan(H / (2*D)) в градусах,
			// с полем frameMargin. Формула `aspect * size` (из оригинала) не масштабируется под
			// разные расстояния — здесь камера рядом с контентом, и прежняя формула давала крайности.
			// Content занимает ~1/frameMargin ширины/высоты рамки. 1.111 → content ~90% рамки.
			// Кадр уже замерен и закреплён: держим его, что бы фигура ни вытворяла. Иначе замах оружием
			// расширил бы габариты, камера отъехала — и тело съёжилось бы на время удара.
			if (_frameLocked)
			{
				transform.localPosition = _lockedPosition;
				face_camera.fieldOfView = _lockedFov;
				return;
			}

			var spriterSprites = transform.Find("Sprites");
			if (spriterSprites != null)
			{
				// Границы фигуры по непрозрачным пикселям — общий замер, тот же, которым подгонщик Spriter
				// нормализует сущность на карте. Включённость частей не спрашиваем: аниматор на отдельных
				// кадрах гасит часть деталей, и кадр портрета от этого прыгал бы.
				Bounds agg;
				bool any = AnimationCacheService.TryGetVisibleBounds(spriterSprites, out agg);
				if (any)
				{
					// Смещаем объект портрета так, чтобы центр mirror-AABB стал в точке камеры (parent.position).
					Vector3 localCenter = transform.parent.InverseTransformPoint(agg.center);
					Vector3 lp = transform.localPosition;
					transform.localPosition = new Vector3(lp.x - localCenter.x, lp.y - localCenter.y, 1);

					// Расстояние камера→контент в WORLD-юнитах (проекция camToContent на cam.forward).
					// InverseTransformPoint не подходит: у face-камеры lossyScale ~(0.24, 0.25, 0.46) из-за UI canvas,
					// local-z не равен world-z. Unity-проекция использует world-distance.
					Vector3 camToContent = agg.center - face_camera.transform.position;
					float worldDistance = Mathf.Abs(Vector3.Dot(camToContent, face_camera.transform.forward));
					if (worldDistance < 0.01f) worldDistance = 0.01f;
					// Margin применяем к целевому видимому размеру (линейно), а не к углу (tan нелинейный).
					// Нужный вертикальный fov: fov = 2 * atan(H_margin / (2 * D)).
					float targetH = agg.size.y * frameMargin;
					float targetW = agg.size.x * frameMargin;
					float needV = 2f * Mathf.Atan(targetH / (2f * worldDistance)) * Mathf.Rad2Deg;
					// Для ширины: tan(fovH/2) = cam.aspect * tan(fovV/2) →  fovV_fromW = 2*atan( (W/aspect) / (2D) ).
					float camAspect = face_camera.aspect > 0.01f ? face_camera.aspect : 1f;
					float needVFromW = 2f * Mathf.Atan(targetW / (camAspect * 2f * worldDistance)) * Mathf.Rad2Deg;
					face_camera.fieldOfView = Mathf.Clamp(Mathf.Max(needV, needVFromW), 1f, 179f);

					if (lockFrame)
					{
						// Габариты те же, что кадром раньше — фигура собралась; поехали — счёт заново.
						if (_measuredHeight > 0
							&& Mathf.Abs(agg.size.y - _measuredHeight) <= _measuredHeight * MEASURE_TOLERANCE)
							_measured++;
						else
							_measured = 0;

						_measuredHeight = agg.size.y;

						if (_measured >= MEASURE_FRAMES)
						{
							_lockedPosition = transform.localPosition;
							_lockedFov = face_camera.fieldOfView;
							_frameLocked = true;
						}
					}

					return;
				}
			}

			// Non-Spriter (статичный fallback-спрайт, warrior для player, unknow для объектов без scml):
			// формула ниже сместит изображение выделелнного предмета так что бы оно оставалось в центре (смещаться будет если pivot отличается от (0.5, 0.5) )
			// Guard: у цели может вообще не быть SpriteRenderer (например когда server-side prefab не
			// определён в library — visual не создан). NRE каждый кадр блокирует остальную работу портрета
			// (в рамке цели — обновление HP), выглядит как "HP не двигается". Молча выходим — кадр пустой,
			// рендерить нечего, но цепочка не валится.
			if (spriteRender == null || spriteRender.sprite == null)
				return;
			Bounds bounds = spriteRender.sprite.bounds;
			Vector2 vector = new Vector2(-bounds.center.x / bounds.extents.x / 2, -bounds.center.y / bounds.extents.y / 2);
			transform.localPosition = new Vector3(vector.x * (spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, vector.y * (spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, 1);

			float max = Mathf.Max(spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit, spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit);

			// эта пропорция изменит отдаленность камерыб число aspect  это контента которая была выситана с учетом размера окна на 1х1 unit размера изображения умножается как раз на юниты размера (считаюстя как текущий размер деленый на pixelsPerUnit)
			face_camera.fieldOfView = aspect * max;
		}
	}
}
