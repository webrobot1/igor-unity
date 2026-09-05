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
		/// <summary>Компоненты, чьи значения существо держит своими полями (см. <see cref="OwnComponent"/>).</summary>
		public const string COMPONENT_HP = "hp";
		public const string COMPONENT_HP_MAX = "hp_max";
		public const string COMPONENT_MP = "mp";
		public const string COMPONENT_MP_MAX = "mp_max";
		public const string COMPONENT_SPEED = "speed";
		public const string COMPONENT_FLEE = "flee";
		public const string COMPONENT_ASSIST = "assist";

		// Добыча: у объекта-сундука и у лавки состав задан самим префабом (loot), у существа он
		// разыгрывается при смерти по таблице дропа (loot_table) — до смерти компонента добычи на нём нет
		// вовсе. Оба приходят в составе префаба (манифест /prefabs). Своими полями существо их не держит:
		// имена лежат здесь как единый список имён компонентов, которыми клиент оперирует.
		public const string COMPONENT_LOOT = "loot";
		public const string COMPONENT_LOOT_TABLE = "loot_table";

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
		[NonSerialized]
		public float? speed = null;

		/// <summary>
		/// Порог бегства: доля максимального здоровья, ниже которой существо бросает бой. null — сервер
		/// про это существо ничего не присылал (вид без такого свойства либо пакет ещё не приезжал).
		/// </summary>
		[NonSerialized]
		public float? flee = null;

		/// <summary>
		/// Радиус зова сородичей на помощь, в клетках. null — как у <see cref="flee"/>.
		/// </summary>
		[NonSerialized]
		public int? assist = null;

        protected override void Awake()
        {
			base.Awake();

			// Error() не бросает — без return следующая строка разыменовала бы тот же null.
			if (lifeBar == null)
			{
				ConnectController.Error("Не указана LifeBar у префаба живого существа " + name);
				return;
			}

			// скороет если при работе со сценой забыли скрыть (оно показается только при выделении на карте существа)
			TargetController.DisableLine(lifeBar);
		}

		public override void SetData(EntityRecive recive)
		{
			this.SetData((CreatureRecive)recive);
		}

        protected void SetData(CreatureRecive recive)
        {
			PrepareComponents(recive.components);
			base.SetData(recive);		
		}

		protected void PrepareComponents(CreatureComponentsRecive components)
        {
			if (components != null)
			{
				if (components.speed != null)
				{
					speed = components.speed;
					//anim.speed = speed;
				}

				if (components.hp != null)
					hp = (int)components.hp;
				if (components.hp_max != null)
					hpMax = (int)components.hp_max;

				if (components.mp != null)
					mp = (int)components.mp;

				// ниже сравниваем c null тк может быть значение 0 которое надо обработать
				if (components.mp_max != null)
					mpMax = (int)components.mp_max;

				if (components.flee != null)
					flee = (float)components.flee;

				if (components.assist != null)
					assist = (int)components.assist;
			}
		}

		/// <summary>
		/// Значение компонента, присланное сервером про ЭТО существо, по slug'у компонента. Единственное
		/// место, где имя компонента сходится со своим полем: показу (окно сведений) нужно спрашивать
		/// свойства по имени, а разбор пакета раскладывает их по типизированным полям.
		/// null — сервер про это существо такого не присылал: свойства нет у вида, компонент приватный
		/// либо пакет ещё не приезжал. Тогда значение берут из каталога префабов — там лежит типовое
		/// для вида (см. AnimationCacheService.GetComponentValue).
		/// Запас (hp_max/mp_max) хранится не как «пришло либо нет», а числом, и ноль в нём значит
		/// «запаса нет» — существо без здоровья либо без маны: такое значение и отдаём как отсутствие,
		/// показывать «0 / 0» незачем.
		/// </summary>
		public virtual float? OwnComponent(string slug)
		{
			switch (slug)
			{
				case COMPONENT_HP: return hp;
				case COMPONENT_HP_MAX: return hpMax > 0 ? hpMax : (float?)null;
				case COMPONENT_MP: return mp;
				case COMPONENT_MP_MAX: return mpMax > 0 ? mpMax : (float?)null;
				case COMPONENT_SPEED: return speed;
				case COMPONENT_FLEE: return flee;
				case COMPONENT_ASSIST: return assist;
			}

			return null;
		}
	}
}
