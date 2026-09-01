using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Живой портрет сущности в интерфейсе: показывает выбранную сущность отдельной камерой, зеркаля её
	/// анимацию (у неанимированных — спрайт). Носителей два и они видны одновременно — рамка цели
	/// и окно информации, — поэтому портрет заведён отдельным классом, а не остался частью рамки.
	///
	/// Ожидаемая раскладка объектов: камера — РОДИТЕЛЬ этого объекта, сам объект несёт SpriteRenderer
	/// (кадр неанимированных) и Animator. Наследник решает, ЧТО показывать (кого выбрали) и по какому
	/// правилу играть анимацию зеркала (<see cref="SyncMirror"/>).
	///
	/// Вид зеркала знают только его сборка (<see cref="ApplyVisual"/>), снятие, замер кадра и повтор
	/// источника; наследнику тело видно четырьмя вопросами — собрано ли (<see cref="MirrorReady"/>),
	/// есть ли клип, какие клипы есть и сыграть.
	/// </summary>
	public abstract class EntityPortrait : MonoBehaviour
	{
		/// <summary>
		/// Пропорция отдаления камеры для НЕанимированных целей: множитель мирового размера спрайта.
		/// У анимированных целей отдаление считается по фактическим границам зеркала (см. CameraUpdate).
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

		// Скелеты Spine исходный (на сущности) и зеркало (дочерним объектом этого UI-GO). Источник нужен не
		// только за именем клипа: у скелета повтор задаёт запускающий, и зеркало держат в ногу с телом ещё
		// и по времени дорожки — иначе одинаковый клип шёл бы у портрета своим ходом.
		private SkeletonAnimation _sourceSkeleton;
		private SkeletonAnimation _mirrorSkeleton;

		protected ObjectModel _target = null;

		protected virtual void Awake()
		{
			animator = GetComponent<Animator>();
			face_camera = transform.parent.GetComponent<Camera>();
			spriteRender = GetComponent<SpriteRenderer>();

			// Error() не бросает: без return портрет доехал бы до работы с этими null'ами
			// (animator — в ApplyVisual, face_camera — в CameraUpdate).
			if (animator == null)
			{
				PlayerController.Error("не наден компонент аниматор в портрете сущности " + name);
				return;
			}

			if (face_camera == null)
			{
				PlayerController.Error("у портрета сущности " + name + " родительский объект не несёт камеру");
				return;
			}
		}

		/// <summary>
		/// Показать сущность: собрать зеркало её тела — скелета Spine, — а у неанимированных скопировать
		/// спрайт. Прежнее зеркало снимается здесь же — метод зовут и при смене цели, и при позднем
		/// появлении тела (оно собирается асинхронно, к моменту выбора его может ещё не быть).
		/// </summary>
		protected void ApplyVisual(ObjectModel value)
		{
			ClearVisual();

			if (value == null)
				return;

			var localSr = spriteRender;
			var srcSkeleton = SourceSkeleton(value);

			if (srcSkeleton != null)
			{
				animator.runtimeAnimatorController = null;

				// Скелет рисуется мешем, и корневой спрайт портрета показывать нечему: границы кадра даёт
				// сам скелет.
				if (localSr != null)
					localSr.enabled = false;

				// Скелет зеркала собирается ТЕМ ЖЕ путём, что тело на карте: второй экземпляр из тех же
				// данных. Размер и посадку кадра он получает оттуда же — у портрета нет ни коллайдера, ни
				// полоски здоровья, и подгонка сводится к приведению фигуры к клетке и к центру объекта,
				// а дальше кадром распоряжается камера (см. CameraUpdate).
				_mirrorSkeleton = VisualBuilder.Create(gameObject, VisualBuilder.Source.Skeleton(
					srcSkeleton.SkeletonDataAsset, null, ClipOf(srcSkeleton)));
				if (_mirrorSkeleton != null)
					// Камера портрета снимает по слою: на общем слое сцены скелет зеркала не попал бы в кадр
					// вовсе, зато встал бы посреди карты.
					_mirrorSkeleton.gameObject.layer = gameObject.layer;

				_sourceSkeleton = srcSkeleton;
				return;
			}

			// Статичный фолбэк для не-анимированных целей.
			animator.runtimeAnimatorController = null;
			if (localSr != null) localSr.enabled = true;
			SpriteRenderer srcSr = value.GetComponentInChildren<SpriteRenderer>(true);
			if (srcSr == null)
				PlayerController.Error("На выбранном объекте налюдения присутвует колайдер но отсутвует Animator и SpriteRenderer");
			if (localSr != null)
				localSr.sprite = srcSr != null ? srcSr.sprite : null;
		}

		/// <summary>
		/// Снять ранее собранное зеркало: без этого следующая цель показывалась бы поверх прежней.
		/// </summary>
		protected void ClearVisual()
		{
			VisualBuilder.Clear(gameObject);
			_sourceSkeleton = null;
			_mirrorSkeleton = null;
			_frameLocked = false;
			_measured = 0;
			_measuredHeight = 0;
		}

		#region Зеркало: вопросы наследника

		/// <summary>Собрано ли зеркало тела. Нет — показан кадр неанимированной цели либо пусто.</summary>
		protected bool MirrorReady => _mirrorSkeleton != null;

		/// <summary>
		/// Само зеркало. Наследнику оно нужно лишь приметой: пересобралось — то, что он снял с прежнего
		/// (перечень клипов, место в переборе), к новому не относится.
		/// </summary>
		protected SkeletonAnimation Mirror => _mirrorSkeleton;

		/// <summary>Есть ли у зеркала такой клип.</summary>
		protected bool MirrorHasClip(string clip)
		{
			if (string.IsNullOrEmpty(clip))
				return false;

			var data = _mirrorSkeleton != null && _mirrorSkeleton.SkeletonDataAsset != null
				? _mirrorSkeleton.SkeletonDataAsset.GetSkeletonData(false)
				: null;
			return data != null && data.FindAnimation(clip) != null;
		}

		/// <summary>Все клипы зеркала — на случай, когда перебирать нечего по библиотеке действий.</summary>
		protected IEnumerable<string> MirrorClips()
		{
			var data = _mirrorSkeleton != null && _mirrorSkeleton.SkeletonDataAsset != null
				? _mirrorSkeleton.SkeletonDataAsset.GetSkeletonData(false)
				: null;
			if (data == null)
				yield break;

			foreach (var animation in data.Animations)
				yield return animation.Name;
		}

		/// <summary>
		/// Запустить клип на зеркале ПО КРУГУ: пока показывается действие, клип идёт снова и снова —
		/// удар, боль и падение видны несколько раз. Повтор тут задаёт показ, а не игра: однократность
		/// действия принадлежит бою на карте, а витрине действий показывать нечего, если клип длиной в
		/// треть секунды один раз мелькнёт и три секунды пролежит стоп-кадром.
		/// </summary>
		protected void MirrorPlay(string clip)
		{
			if (_mirrorSkeleton != null)
				_mirrorSkeleton.AnimationState.SetAnimation(0, clip, true);
		}

		/// <summary>Имя клипа, играющего на зеркале; null — зеркала нет либо оно ничего не играет.</summary>
		protected string MirrorClip => ClipOf(_mirrorSkeleton);

		#endregion

		/// <summary>Скелет тела сущности; null — тело собрано иначе либо ещё не собрано.</summary>
		private static SkeletonAnimation SourceSkeleton(ObjectModel value)
		{
			var skeleton = value.GetComponentInChildren<SkeletonAnimation>();
			return skeleton != null && skeleton.SkeletonDataAsset != null ? skeleton : null;
		}

		/// <summary>Имя клипа, играющего на скелете; null — не играет ничего.</summary>
		private static string ClipOf(SkeletonAnimation skeleton)
		{
			var track = TrackOf(skeleton);
			return track != null && track.Animation != null ? track.Animation.Name : null;
		}

		/// <summary>Дорожка клипа скелета — та единственная, которой тело и управляют.</summary>
		private static Spine.TrackEntry TrackOf(SkeletonAnimation skeleton)
		{
			return skeleton != null && skeleton.AnimationState != null
				? skeleton.AnimationState.GetTrack(0)
				: null;
		}

		/// <summary>
		/// Пер-кадровая работа портрета: поздняя сборка зеркала, обновление кадра статичной цели,
		/// правило анимации зеркала и наводка камеры. Наследник зовёт это, пока цель показана.
		/// </summary>
		protected void PortraitUpdate()
		{
			if (_target == null)
				return;

			// Тело могло собраться уже ПОСЛЕ выбора (анимация грузится асинхронно): при выборе его не было,
			// ушли в ветку статичного кадра. Ловим появление и пересобираем зеркало.
			if (SourceSkeleton(_target) != null && !MirrorReady)
			{
				ApplyVisual(_target);
				return;
			}

			// Статичная цель (kind-only/image, без зеркала и без Animator-контроллера): спрайт источника
			// меняется в рантайме — Universal dead/remove перезаписывает его (placeholder → dead-кадр), а
			// restore возвращает обратно. Без пер-кадровой синхронизации портрет показывает устаревший
			// снимок (unknow при лежащем dead-черепе).
			if (animator.runtimeAnimatorController == null && !MirrorReady && spriteRender != null)
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
			if (_mirrorSkeleton == null)
				return;

			var source = TrackOf(_sourceSkeleton);
			if (source == null || source.Animation == null)
				return;

			var mirror = TrackOf(_mirrorSkeleton);
			if (mirror == null || mirror.Animation == null || mirror.Animation.Name != source.Animation.Name)
			{
				if (!MirrorHasClip(source.Animation.Name))
					return;

				mirror = _mirrorSkeleton.AnimationState.SetAnimation(
					0, source.Animation.Name, source.Loop);
			}

			// Держим и ВРЕМЯ: у скелета клип идёт своим ходом от запуска, и одинаковое имя ещё не значит
			// одинаковой позы — портрет отставал бы от тела на карте тем сильнее, чем дольше показан.
			mirror.TrackTime = source.TrackTime;
		}

		/// <summary>
		/// Мировые границы фигуры зеркала в её текущей позе — по ним и наводится кадр. Меряет их сам скелет
		/// по своим кускам: спрайтов у него нет вовсе, и общий замер по спрайтам дал бы пустоту.
		/// Включённость частей не спрашиваем: клип на отдельных кадрах гасит часть деталей, и кадр
		/// портрета от этого прыгал бы.
		/// </summary>
		private bool TryGetMirrorBounds(out Bounds bounds)
		{
			if (_mirrorSkeleton != null)
				return VisualBuilder.TryGetWorldBounds(_mirrorSkeleton, out bounds);

			bounds = new Bounds();
			return false;
		}

		// если изображения анимации с сильно отличабщимеся pivot to возможно надо будет каждый FixedUpdate делать этот метод для пересчета положения камеры и объекта что бы он не выходил за рамки
		protected void CameraUpdate()
		{
			// Анимированная цель: показана зеркалом её тела.
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

			if (TryGetMirrorBounds(out Bounds agg))
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

			// Зеркала нет (статичный fallback-спрайт, warrior для player, unknow для объектов без анимации):
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
