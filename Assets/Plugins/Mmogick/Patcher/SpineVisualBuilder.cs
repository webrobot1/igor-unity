using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Сборка визуала сущности из скелета Spine: собирать почти нечего — рантайм сам строит скелет по
	/// данным, нам остаётся размер и клип.
	///
	/// Скелет живёт ДОЧЕРНИМ объектом сущности: корневой трансформ ведёт сама сущность (зеркалит себя по X
	/// при смене направления), и масштаб тела на нём задавать нельзя — он затёрся бы следующим поворотом.
	///
	/// Высота тела приводится к доле клетки, которую объявил сервер; сами габариты фигуры берутся замером
	/// у скелета — в его собственных единицах они у каждого свои.
	/// </summary>
	public static class SpineVisualBuilder
	{
		/// <summary>Высота тела на сцене — одна клетка, как у прежней сборки.</summary>
		public const float TARGET_HEIGHT = 1.0f;

		/// <summary>Имя дочернего объекта со скелетом: по нему же он и сносится при смене визуала.</summary>
		public const string CHILD = "Skeleton";

		/// <summary>
		/// Собрать скелет на сущности. Прежний визуал сносится: смена префаба меняет и скелет.
		/// Клип задаётся вызывающим — он знает действие сущности; пусто — берётся первый клип скелета.
		/// </summary>
		public static SkeletonAnimation Create(GameObject go, SkeletonDataAsset asset, float? serverSize, string clip)
		{
			if (go == null || asset == null) return null;

			Clear(go);

			var child = new GameObject(CHILD);
			child.transform.SetParent(go.transform, false);

			// Компонентов у скелета ДВА — сам он и его рисователь, — и ставит их фабрика рантайма: добавленный
			// в одиночку компонент не находит рисователя и падает на первом же обращении к данным.
			var animation = SkeletonAnimation.AddToGameObject(child, asset).skeletonAnimation;
			if (animation == null || animation.Skeleton == null) return null;

			var data = asset.GetSkeletonData(false);
			string first = data != null && data.Animations.Count > 0 ? data.Animations.Items[0].Name : null;
			string wanted = !string.IsNullOrEmpty(clip) && data != null && data.FindAnimation(clip) != null ? clip : first;
			// Повтор задаёт запускающий: у формата Spine его в скелете нет, перечень неповторяемых клипов
			// (смерть, удар) приходит с пакетом скелета.
			if (!string.IsNullOrEmpty(wanted))
				animation.AnimationState.SetAnimation(0, wanted, SpineCacheService.Loops(asset, wanted));

			Fit(go, child.transform, animation, serverSize);
			return animation;
		}

		/// <summary>
		/// Мировые границы фигуры скелета в её ТЕКУЩЕЙ позе. Замер идёт у самого скелета — по кускам,
		/// которые он сейчас показывает: спрайтов у скелета нет вовсе, он рисуется мешем, и замер по
		/// спрайтам на нём дал бы пустоту.
		/// Одним моментом, не проходом по клипу (<see cref="Measure"/>): вызывающий смотрит на живую позу.
		/// false — скелета нет либо в этой позе не видно ни куска.
		/// </summary>
		public static bool TryGetWorldBounds(SkeletonAnimation animation, out Bounds bounds)
		{
			bounds = new Bounds();
			if (animation == null || animation.Skeleton == null) return false;

			animation.Skeleton.GetBounds(out float x, out float y, out float w, out float h, ref _boundsBuffer);
			if (w <= 0.0001f || h <= 0.0001f) return false;

			// Углы прямоугольника переводим по одному: скелет бывает повёрнут и отражён по оси, и перевод
			// одного лишь центра с размером дал бы рамку от чужой позы.
			var t = animation.transform;
			bounds = new Bounds(t.TransformPoint(new Vector3(x, y, 0f)), Vector3.zero);
			bounds.Encapsulate(t.TransformPoint(new Vector3(x + w, y, 0f)));
			bounds.Encapsulate(t.TransformPoint(new Vector3(x, y + h, 0f)));
			bounds.Encapsulate(t.TransformPoint(new Vector3(x + w, y + h, 0f)));
			return true;
		}

		// Рабочий буфер замера границ. Поле, а не локальный массив: замер идёт каждый кадр у каждого
		// показанного портрета, и свой массив на вызов давал бы мусор кадра на ровном месте.
		private static float[] _boundsBuffer = new float[8];

		/// <summary>Снести скелет сущности — при смене визуала на другой скелет либо на картинку.</summary>
		public static void Clear(GameObject go)
		{
			if (go == null) return;
			var child = go.transform.Find(CHILD);
			if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
		}

		/// <summary>Зазор от макушки до полоски здоровья в мировых единицах — как у сборки из дерева.</summary>
		private const float LIFE_BAR_MARGIN = 0.25f;

		/// <summary>Сколько моментов клипа проходит замер: по ним берётся середина ряда.</summary>
		private const int MEASURE_SAMPLES = 8;

		/// <summary>
		/// Тело в клетку и по месту. Правила те же, что у сборки из картинки (<see cref="UpdateController"/>),
		/// — иначе одна и та же сущность на двух путях выглядела бы разного размера и стояла бы по-разному:
		///
		/// РАЗМЕР — по БОЛЬШЕЙ стороне габаритов, не по высоте: вытянутый поперёк (краб, корабль) иначе вылез
		/// бы за клетку шириной. Сами габариты замеряются у скелета всегда: серверное значение габаритом не
		/// является — оно ДЕЛИТЕЛЬ целевой высоты (высота тела в клетках задаётся на сервере, на клиент уходит
		/// обратная ей величина), тот же контракт у сборки из картинки — <see cref="UpdateController"/>.
		/// Своего размера у сущности нет — цель остаётся в клетку.
		///
		/// МЕСТО — центр фигуры на центр коллайдера, которым по сущности и щёлкают: скелет рисуется от своей
		/// точки отсчёта, и без сдвига фигура стоит там, где её посадил художник, а не там, где её ловит мышь.
		///
		/// Замер идёт по ИГРАЮЩЕМУ клипу, не по позе покоя: у покоя руки бывают раскинуты, и тело по ней вышло
		/// бы заметно мельче. Клип проходится несколькими моментами, размер берётся серединой ряда: за клип
		/// фигура то вытягивается, то сжимается, и один момент дал бы случайный размер. Поза считается
		/// прямо тут, кадров игры замер не ждёт.
		/// </summary>
		private static void Fit(GameObject go, Transform child, SkeletonAnimation animation, float? serverSize)
		{
			Measure(animation, out float width, out float height, out Vector2 middle);

			float span = Mathf.Max(width, height);
			// Замер пуст — в этой позе не показано ни куска: масштаб не трогаем, ставить его не от чего.
			if (span <= 0.0001f) return;

			float targetHeight = serverSize.HasValue && serverSize.Value > 0.0001f
				? TARGET_HEIGHT / serverSize.Value
				: TARGET_HEIGHT;

			float parent = child.parent != null ? Mathf.Abs(child.parent.lossyScale.y) : 1f;
			if (parent < 0.0001f) parent = 1f;

			float scale = targetHeight / (parent * span);
			child.localScale = new Vector3(scale, scale, 1f);

			Vector2 center = middle * scale;

			var capsule = go.GetComponent<CapsuleCollider2D>();
			Vector2 target = capsule != null ? capsule.offset : Vector2.zero;
			child.localPosition = new Vector3(target.x - center.x, target.y - center.y, child.localPosition.z);

			var lifeBar = go.transform.Find("LifeBar");
			if (lifeBar == null) return;

			// Зазор задан в мировых единицах — у сущностей разный масштаб корня, и одинаковым он смотрится
			// только после перевода в её собственные.
			float rootScale = Mathf.Max(Mathf.Abs(go.transform.lossyScale.y), 0.0001f);
			float top = child.localPosition.y + center.y + height / 2f * scale;
			Vector3 barPosition = lifeBar.localPosition;
			barPosition.y = top + LIFE_BAR_MARGIN / rootScale;
			lifeBar.localPosition = barPosition;
		}

		/// <summary>
		/// Габариты фигуры и её середина в единицах скелета: клип проходится равными долями длительности,
		/// по каждому ряду берётся середина. Поза считается прямо тут, кадров игры замер не ждёт.
		/// Клипа нет — остаётся поза покоя, других данных у скелета в этот момент не существует.
		/// </summary>
		private static void Measure(SkeletonAnimation animation, out float width, out float height, out Vector2 middle)
		{
			var skeleton = animation.Skeleton;
			var track = animation.AnimationState != null ? animation.AnimationState.GetTrack(0) : null;
			var clip = track != null ? track.Animation : null;
			float duration = clip != null ? clip.Duration : 0f;

			var widths = new List<float>(MEASURE_SAMPLES);
			var heights = new List<float>(MEASURE_SAMPLES);
			var centersX = new List<float>(MEASURE_SAMPLES);
			var centersY = new List<float>(MEASURE_SAMPLES);
			float[] vertices = new float[8];

			int samples = clip != null && duration > 0.0001f ? MEASURE_SAMPLES : 1;
			for (int i = 0; i < samples; i++)
			{
				skeleton.SetupPose();
				if (clip != null)
				{
					float time = duration * i / samples;
					clip.Apply(skeleton, time, time, false, null, 1f, MixFrom.Setup, false, false, false);
				}
				skeleton.UpdateWorldTransform(Spine.Physics.Update);
				skeleton.GetBounds(out float x, out float y, out float w, out float h, ref vertices);
				if (w <= 0.0001f || h <= 0.0001f) continue;

				widths.Add(w);
				heights.Add(h);
				centersX.Add(x + w / 2f);
				centersY.Add(y + h / 2f);
			}

			width = Median(widths);
			height = Median(heights);
			middle = new Vector2(Median(centersX), Median(centersY));
		}

		/// <summary>Середина ряда. Ряд пуст — нуль: вызывающий на нём и остановится.</summary>
		private static float Median(List<float> values)
		{
			if (values.Count == 0) return 0f;
			values.Sort();
			return values[values.Count / 2];
		}
	}
}
