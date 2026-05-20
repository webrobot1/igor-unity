using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;
using System;

namespace Mmogick
{
	/// <summary>
	/// объекты могут быть не анимированы. враги и игроки что анследуют этот класс - обязательно должны иметь анмицию + модель статистики (жизни и тп)
	/// </summary>
	public class EnemyModel : ObjectModel
	{
		[Header("Для работы с значками состояния существа")]
		/// <summary>
		/// поле с жизнями выделленого существа
		/// </summary>
		public Image lifeBar;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект оно именно тут для совместимости как и то что ниже
		/// </summary>
		[NonSerialized]
		public int? hp = null;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект
		/// </summary>
		[NonSerialized]
		public int? mp = null;

		[NonSerialized]
		public int hpMax;

		[NonSerialized]
		public int mpMax;

		/// <summary>
		/// в основном используется для живых существ но если предмет что то переместит то у него тоже должна быть скорость
		/// </summary>
		protected float speed;

        protected override void Awake()
        {
			base.Awake();
			if (lifeBar == null)
				ConnectController.Error("Ну казкана LifeBar у префабов живых существ");

			// скороет если при работе со сценой забыли скрыть (оно показается только при выделении на карте существа) 
			TargetController.DisableLine(lifeBar);
		}

        protected void Start()
		{		
			if (lifeBar == null)
				PlayerController.Error("Не найдено в группе поле статистики сущности "+key);
		}

		public override void SetData(EntityRecive recive)
		{
			this.SetData((EnemyRecive)recive);
		}

        protected void SetData(EnemyRecive recive)
        {
			PrepareComponents(recive.components);
			base.SetData(recive);		
		}

		protected void PrepareComponents(EnemyComponentsRecive components)
        {
			if (components != null)
			{
				if (components.speed != null)
				{
					speed = (int)components.speed;
					//anim.speed = speed;
				}

				if (components.hp != null)
				{
					if (hp == 0 && components.hp > 0)
					{
						Resurrect();
					}
					else if (components.hp == 0)
					{
						Dead();
					}

					hp = (int)components.hp;
				}
				if (components.hp_max != null)
					hpMax = (int)components.hp_max;

				if (components.mp != null)
					mp = (int)components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (components.mp_max != null)
					mpMax = (int)components.mp_max;
			}
		}

		protected virtual void Dead()
        {

        }

		protected virtual void Resurrect()
        {

		}

		// Применяет альфу ко ВСЕМ SpriteRenderer'ам под сущностью (включая Spriter-детей).
		// Кеш в Awake недопустим: wrap/Spriter могут пересоздать структуру в любой момент.
		private void SetSpritesAlpha(float alpha)
		{
			foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
			{
				var c = sr.color;
				sr.color = new Color(c.r, c.g, c.b, alpha);
			}
		}

		// «Призрачный» режим: hp=0 и action != dead (труп лежит при action=dead с полной альфой,
		// а двигается призраком — полупрозрачно). Условие пересчитывается каждый кадр в LateUpdate,
		// поэтому переключения action ↔ dead/walk/idle подхватываются автоматически без отдельных
		// триггеров от SetData. SpriterDotNet.UnityAnimator каждый кадр в ApplySpriteTransform
		// перезаписывает SpriteRenderer.color по info.Alpha из SCML — поэтому однократный SetAlpha
		// затирается на следующем Update. LateUpdate идёт после всех Update в кадре и держит alpha.
		// Живёт в EnemyModel (а не в PlayerModel), чтобы enemy/animal тоже становились призраками
		// при hp=0 — поле hp объявлено здесь.
		void LateUpdate()
		{
			if (hp == 0 && action != "dead")
				SetSpritesAlpha(0.5f);
		}
	}
}
