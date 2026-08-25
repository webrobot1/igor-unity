using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Это класс вешающиеся на префабы существ . Метод setData оббновляет у существа данные (в качестве агрумента - пакет который пришел от сервера по конкретному существу), а также вспомогательные методы (оставшееся время до вызова команды, данные о кокнретном событие которое повешано на существо, вызвать лог с авто подстановкой key существа в начале) 
	/// </summary>
	public class EntityModel : MonoBehaviour
	{
		/// <summary>
		/// для того что бы менять сортировку при загрузке карты
		/// </summary>
		[NonSerialized]
		public int sort;

		[NonSerialized]
		public int lifeRadius;

		/// <summary>
		/// индентификатор сущности
		/// </summary>
		[NonSerialized]
		public string key;		
		
		/// <summary>
		/// тип сущности
		/// </summary>
		[NonSerialized]
		public string type;

		/// <summary>
		/// может изменится в процессе игры (переход на другую локацию)
		/// </summary>
		[NonSerialized]
		public int map;

		[NonSerialized]
		public string slug;

		/// <summary>
		/// стандартное поле действия. хорошим тоном связать его с анимацией и в серверных механиках использовать
		/// </summary>
		[NonSerialized]
		public string action = "idle";

		[NonSerialized]
		public string prefab;

		/// <summary>
		/// Когда сущность заведена на сервере (у игрока — дата регистрации) и когда её данные меняли в
		/// последний раз (у игрока — последний вход). Строки ISO-8601 как пришли с сервера; показывает их
		/// окно информации о цели, разбирая в местное время при показе.
		/// </summary>
		[NonSerialized]
		public string created;

		[NonSerialized]
		public string updated;

		// Готовое имя и то, из чего оно собрано. Имя спрашивают каждый кадр (надпись под курсором, рамка
		// цели), а сборка режет строки — пересобираем только когда сменилось исходное.
		private string _displayName;
		private string _displayNameOfPrefab;
		private string _displayNameOfSlug;

		/// <summary>
		/// Имя сущности для подписей интерфейса (надпись в мире, рамка цели, очередь на добычу).
		/// У игрока это логин аккаунта: сервер кладёт его в <see cref="slug"/>. У прочих — имя prefab'а
		/// из серверной library, заданное в админке; не задано — сам slug prefab'а, он тоже читаем.
		/// Единая точка: имена сущностей у всех фронтов интерфейса должны совпадать.
		/// </summary>
		public string DisplayName
		{
			get
			{
				if (_displayName == null || _displayNameOfPrefab != prefab || _displayNameOfSlug != slug)
				{
					_displayNameOfPrefab = prefab;
					_displayNameOfSlug = slug;
					_displayName = BuildDisplayName();
				}

				return _displayName;
			}
		}

		private string BuildDisplayName()
		{
			if (type == "player")
				return !string.IsNullOrEmpty(slug) ? slug : key;

			if (string.IsNullOrEmpty(prefab))
				return !string.IsNullOrEmpty(slug) ? slug : key;

			return CleanName(AnimationCacheService.GetPrefabName(prefab) ?? prefab);
		}

		/// <summary>
		/// Имя prefab'а в library задаётся именем файла картинки ("iron_sword.png"), а игроку показывают
		/// вещь, а не файл: снимаем расширение и разделяем слова.
		/// </summary>
		private static string CleanName(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return raw;
			int dot = raw.LastIndexOf('.');
			if (dot > 0) raw = raw.Substring(0, dot);
			return raw.Replace('_', ' ');
		}

		/// <summary>
		/// Placeholder-спрайт kind-only сущности (нет ни image, ни скелета — рисуется "unknow" с Resources-префаба).
		/// Запоминается в UpdateController.ApplyVisualPrefab. Нужен чтобы ВЕРНУТЬ его на корневой SpriteRenderer
		/// после терминального Universal-эффекта: dead/remove.anim перезаписывают SR.m_Sprite (PPtrCurve,
		/// writeDefaults=false) и сами назад не откатывают — без восстановления сущность, ожившая после dead,
		/// навсегда застревала бы на dead-кадре. Восстановление выполняет PlayAction (см. Universal-ветку).
		/// null для сущностей с картинкой и со скелетом (у них свой визуал, восстанавливать нечего).
		/// </summary>
		private Sprite _fallbackSprite;

		/// <summary>
		/// Запоминает placeholder-спрайт kind-only сущности для восстановления после Universal-эффекта.
		/// Вызывается из UpdateController.ApplyVisualPrefab ДО навешивания Animator'а (когда на корневом SR
		/// ещё placeholder-спрайт). Мировой РАЗМЕР эффекта кодом не подгоняется — он настраивается через
		/// Pixels Per Unit в import-настройках спрайтов эффекта (Sprites/Entitys/Dead/*.png и т.п.):
		/// размер = пиксели / ppu × scale kind-префаба. См. <see cref="_fallbackSprite"/>.
		/// </summary>
		public void SetFallbackSprite(Sprite sprite) => _fallbackSprite = sprite;

		/// <summary>
		/// Живой спавн (существо появилось, пока игрок в игре) — сыграть эффект появления, когда визуал
		/// будет готов и показан. Ставит UpdateController.UpdateObject в спавн-ветке; гейт (не отгрузка
		/// мира, не свой игрок, не object) — там же. Снимается единожды в <see cref="OnVisualReady"/>.
		/// </summary>
		[NonSerialized]
		public bool pendingAppearFlash;

		/// <summary>
		/// LifeBar скрыт на время асинхронной сборки скелета (UpdateController.ApplyVisualPrefab
		/// прячет его вместе с placeholder-спрайтом, чтобы полоска не висела в воздухе без тела).
		/// Возврат — в <see cref="OnVisualReady"/> либо в error-ветке загрузки самим ApplyVisualPrefab.
		/// </summary>
		[NonSerialized]
		public bool lifeBarHiddenForBuild;

		/// <summary>
		/// Точка «визуал сущности готов и показан»: конец сборки скелета у animation-prefab'ов,
		/// синхронное применение у image/kind-only (UpdateController.ApplyVisualPrefab).
		/// Возвращает спрятанный на время сборки LifeBar и играет отложенный эффект появления.
		/// Повторные вызовы (смена prefab на лету) безопасны: оба флага одноразовые.
		/// </summary>
		public void OnVisualReady()
		{
			if (lifeBarHiddenForBuild)
			{
				lifeBarHiddenForBuild = false;
				var lifeBar = transform.Find("LifeBar");
				if (lifeBar != null) lifeBar.gameObject.SetActive(true);
			}

			if (pendingAppearFlash)
			{
				pendingAppearFlash = false;
				SpawnAppearFlash();
			}
		}

		/// <summary>
		/// Эффект появления существа: ОТДЕЛЬНЫЙ дочерний объект с Universal Animator'ом, играющий Puff
		/// (кадры и state'ы remove) поверх уже показанного тела. Именно отдельный объект: Universal-ветка
		/// PlayAction на самой сущности прячет тело — для появления оно должно оставаться видимым.
		/// Позиция/масштаб — системы корня сущности, как у remove-Puff на корневом SR.
		/// Уничтожается сам по концу анимации (AppearFlashEffect).
		/// </summary>
		private void SpawnAppearFlash()
		{
			var controller = GetUniversalController();
			if (controller == null) return;   // Universal-ассет отсутствует — предупреждение уже выдано

			var fx = new GameObject("AppearFlash");
			fx.transform.SetParent(transform, false);

			var sr = fx.AddComponent<SpriteRenderer>();
			// Поверх всего, что нарисовано на сущности внутри её SortingGroup.
			sr.sortingOrder = 1000;

			var anim = fx.AddComponent<Animator>();
			anim.runtimeAnimatorController = controller;
			anim.SetInteger("direction", ForwardToDirection());
			anim.SetTrigger(ConnectController.ACTION_REMOVE);

			fx.AddComponent<AppearFlashEffect>();
		}

		private Vector3 _forward = Vector3.zero;

		/// <summary>
		/// Нарисованный угол текущего клипа тела (0=вправо, 90=вверх) — ключ angle-карты
		/// actions (из /prefabs entry), под которым клип отрезолвлен в GetClipName. null — клип без направления
		/// или резолв не удался (fallback на имя action). Зеркало (flipX) сюда НЕ входит — оно живёт
		/// в localScale корня, потребитель читает его из lossyScale. Нужен WeaponMount для выбора
		/// варианта картинки предмета по ФАКТИЧЕСКОМУ ракурсу тела, а не по логическому Forward:
		/// ракурсов может быть меньше, чем направлений (существо с единственным фронтальным видом
		/// всегда играет его, какой бы Forward ни был — предмет обязан следовать за телом).
		/// </summary>
		[NonSerialized]
		public int? DisplayAngle;

		/// <summary>
		/// при запросе поля выдает серверные значения. при смене - меняет transform position только в клиенте (на сервере меняется лишь попутно с другими событиями требующих направления)
		/// </summary>
		public virtual Vector3 Forward
		{
			get { return _forward; }
			set
			{
				// нельзя менять кроме как по данным с сервера тк нужно для расчета движимся ли мы или телепортируемся
			}
		}

		// когда последний раз обновляли данные (для присвоения action - idle по таймауту)
		protected DateTime activeLast = DateTime.Now;

		private Dictionary<string, Event> events = new Dictionary<string, Event>();

		/// <summary>
		/// координаты в которых  уже находится наш объект на сервере (может не совпадать с позицией префаба тк анимация сглаживает скачки перехода и позиция изменяется постепенно в игре)
		/// </summary>
		[NonSerialized]
		public Vector3 position = Vector3.zero;

		/// <summary>
		/// Группа сортировки сущности (на корне) и холст полоски жизни (в потомках). Держим ссылки, потому что
		/// порядок отрисовки выставляется на каждый приходящий пакет, а поиск компонента — обход объекта, поиск
		/// холста — обход всех его потомков. Состав компонентов после сборки визуала не меняется.
		/// </summary>
		[NonSerialized]
		public UnityEngine.Rendering.SortingGroup sortingGroup;

		[NonSerialized]
		public Canvas barCanvas;

		/// <summary>
		/// Найти ссылки на компоненты отрисовки, если они ещё не найдены (или потерялись при пересборке визуала).
		/// </summary>
		public void EnsureRenderRefs()
		{
			if (sortingGroup == null)
				sortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();

			if (barCanvas == null)
				barCanvas = GetComponentInChildren<Canvas>(true);
		}

		/// <summary>
		/// Одна ли клетка у двух мировых позиций. Клетка = округление координат: banker's rounding
		/// (Mathf.RoundToInt) зеркалит серверный position.tile(). Единый источник клеточного порога
		/// для клиентских гейтов «на той же клетке, что сущность» (открытие контейнера, авто-закрытие
		/// его окна и т.п.) — чтобы зеркало не разошлось с серверным same-tile в разных местах.
		/// Сравнивать серверную position (авторитетную), не сглаженный transform.position.
		/// </summary>
		public static bool SameTile(Vector3 a, Vector3 b)
		{
			return Mathf.RoundToInt(a.x) == Mathf.RoundToInt(b.x)
				&& Mathf.RoundToInt(a.y) == Mathf.RoundToInt(b.y);
		}

		/// <summary>
		/// Сколько раз сущности приходили данные с сервера. Признак «здесь что-то изменилось» для тех, кто
		/// СОБИРАЕТ по сущности показ целиком и не может собирать его дёшево: окно сведений строит свои
		/// строки общим правилом «своё значение, иначе типовое для вида», перебирая перечень свойств и
		/// разбирая справочники, — такой сбор на каждом кадре стоит дороже всего, что он показывает.
		///
		/// Признак ПОЛНЫЙ: данные сущности меняет только <see cref="SetData"/>, а её саму — только пакет
		/// сервера. Существо вне чужого поля зрения стоит, пакетов по нему не приходит, и версия не
		/// двигается — потребителю в это время пересчитывать нечего, и он честно не делает ничего.
		///
		/// Не путать с версией добычи (CorpseLootMarker.Version): та про содержимое КОНТЕЙНЕРА и двигается
		/// его составом, эта — про саму сущность и двигается любым её пакетом.
		/// </summary>
		public int Version { get; private set; }

		/// <summary>
		/// установка данных пришедших с сервера объекту
		/// </summary>
		public virtual void SetData(EntityRecive recive)
		{
			if (recive.map != null)
			{
				this.map = (int)recive.map;
			}

			if (recive.action != null)
			{
				activeLast = DateTime.Now;

				// пришла команды удаления с карты объекта
				if (recive.action == ConnectController.ACTION_REMOVE) 
				{ 
					if (action != recive.action)
                    {
						action = recive.action;
						StartCoroutine(this.Remove(recive.map != null));
					}
                    else
                    {
						LogError("Существо сменило карту, но было удалено на новой в том же кадре что и добавлено");
						StartCoroutine(this.Destroy());
					}	
				}
                else
                {
					// Спрашиваем тело именно этой сущности, а не первое найденное в сцене
					if (HasBody)
					{
						string prefabName = !string.IsNullOrEmpty(recive.prefab) ? recive.prefab : this.prefab;
						float fwdX = recive.forwardX ?? Forward.x;
						float fwdY = recive.forwardY ?? Forward.y;
						var (clipName, flipX, clipAngle) = AnimationCacheService.GetClipName(
							prefabName, recive.action, fwdX, fwdY);
						if (clipName == null) { clipName = recive.action; flipX = false; }

						// Действие, которым клип выбран: повтор берётся у НЕГО, и при подмене клипа ниже
						// подменяется вместе с клипом — у idle свой повтор, к пришедшему action отношения
						// не имеющий.
						string clipAction = recive.action;

						// У action нет своего клипа (ACTION_LOAD, не настроенный action и т.п.) — fallback на
						// idle_action, иначе тело осталось бы на первом клипе набора (у player'а это Attack).
						if (!BodyHasClip(clipName))
						{
							var (idleClip, idleFlip, idleAngle) = AnimationCacheService.GetClipName(
								prefabName, ConnectController.idle_action, fwdX, fwdY);
							if (BodyHasClip(idleClip))
							{
								clipName = idleClip;
								flipX = idleFlip;
								clipAngle = idleAngle;
								clipAction = ConnectController.idle_action;
							}
						}

						// Ракурс тела — только если клип у тела реально есть (иначе играет прежний клип,
						// и прежний DisplayAngle остаётся верным).
						if (BodyHasClip(clipName))
							DisplayAngle = clipAngle;

						if (type != "object")
						{
							Vector3 s = transform.localScale;
							transform.localScale = new Vector3(
								flipX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
						}

						bool changed = action != recive.action;
						bool nonLoop = BodyClip != null && !BodyClipLoops;
						bool animationDiverged = BodyClip != clipName;
						if (changed || nonLoop || animationDiverged)
						{
							if (BodyHasClip(clipName))
								BodyPlay(clipName);
							else
								LogWarning("Анимация: клипа '" + clipName + "' (action '" + recive.action + "') у тела нет");
						}
					}
					action = recive.action;
				}
			}
			
			if (recive.forwardX != null || recive.forwardY != null)
            {
				Vector3 vector = new Vector3(recive.forwardX ?? Forward.x, recive.forwardY ?? Forward.y, 0);

				if (vector.x != _forward.x || vector.y != _forward.y)
				{
					Forward = vector;
					_forward = vector;

				// Forward сменился без смены action — ре-резолв направленного clip.
					// На первом спавн-пакете this.prefab ещё НЕ присвоен (присваивается ниже из recive.prefab),
					// а recive.prefab уже есть — берём его приоритетно (как в action-блоке выше). Иначе и
					// Spriter-re-resolve, и rotatable-gate на спавне работают по пустому prefab: снаряд получает
					// forward один раз при спавне, _forward сразу записывается, а поворот по пустому prefab
					// пропускается → последующие пакеты с тем же forward ветку не триггерят → снаряд не крутится.
					string pn = !string.IsNullOrEmpty(recive.prefab) ? recive.prefab : this.prefab;
					if (HasBody && !string.IsNullOrEmpty(pn))
					{	
						if (action != null && recive.action == null)
						{
							var (newClip, newFlip, newAngle) = AnimationCacheService.GetClipName(
								pn, action, Forward.x, Forward.y);
							if (newClip != null)
							{
								Vector3 s = transform.localScale;
								transform.localScale = new Vector3(
									newFlip ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
								if (BodyHasClip(newClip))
								{
									DisplayAngle = newAngle;
									if (BodyClip != newClip)
										BodyPlay(newClip);
								}
							}
						}
					}
					// Поворот transform применяем только сущностям без собранного тела и без legacy blend-tree
					// Animator'а.
					// У player/enemy/animal направление передаётся сменой clip + flip по X (Spriter), либо
					// SetFloat("x"/"y") в blend-tree (исторический legacy-механизм, сам blend-tree контроллер
					// удалён) — им крутить transform нельзя.
					// Universal.controller (overlay для remove-эффектов) не имеет параметров x/y — для image-
					// projectile'ов с ним крутить ВСЁ ЕЩЁ можно.
					// Критерий «нельзя крутить»: есть тело с направленными клипами, либо есть Animator
					// с параметрами x/y.
					// Конвенция: канонический спрайт нарисован под PrefabEntry.angle (обычно вправо, 0).
					// Atan2(y,x) — угол от оси X+; вычитаем опорный angle, чтобы спрайт любого ракурса
					// смотрел остриём по курсу. Server default forward=(0,-1) → спрайт смотрит вниз.
					// Gate: rotationMode == Free (админка → GameImage.rotationMode, бывший rotatable=true).
					// Без него все статичные image-prefab'ы (apple, sword) крутились бы вслед за forward,
					// что выглядит неестественно — крутятся только явно отмеченные (фаерболы, стрелы).
					else if (!HasBody
						&& AnimationCacheService.GetPrefabRotationMode(pn) == AnimationCacheService.RotationMode.Free)
					{
						// «Можно крутить» = нет legacy Animator с blend-tree параметрами x/y.
						// Universal.controller имеет только direction/remove — projectile'и с ним крутить можно.
						bool blendTree = false;
						var rotAnim = GetComponent<Animator>();
						if (rotAnim != null && rotAnim.runtimeAnimatorController != null)
						{
							foreach (var p in rotAnim.parameters)
								if (p.name == "x" || p.name == "y") { blendTree = true; break; }
						}
						if (!blendTree)
							transform.rotation = Quaternion.Euler(0, 0,
								Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg - AnimationCacheService.GetPrefabAngle(pn));
					}
				}
			}


			if (recive.x != null)
			{
				position.x = (float)recive.x;
			}

			if (recive.y != null)
			{
				position.y = (float)recive.y;
			}

			if (recive.z != null)
			{
				position.z = (float)recive.z;
			}

			if (recive.sort != null)
				this.sort = (int)recive.sort;

			if (recive.lifeRadius != null)
				this.lifeRadius = (int)recive.lifeRadius;

			if (!string.IsNullOrEmpty(recive.prefab))
				this.prefab = recive.prefab;

			if (recive.slug != null)
				this.slug = recive.slug;

			if (recive.created != null)
				this.created = recive.created;

			if (recive.updated != null)
				this.updated = recive.updated;

			if (recive.events!=null && recive.events.Count > 0)
			{
				foreach (KeyValuePair<string, EventRecive> kvp in recive.events)
				{
					Event ev = getEvent(kvp.Key);

					// если мы сбрасяваем таймаут (например из каких то механик) - придет это поле (оно придет кстати и при таймауте события и может еще более точно скорректировать время таймаута)
					if (kvp.Value.remain != null) 
					{
						// вычтем время которое понадобилось что бы дойти ответу (половину пинга)
						ev.finish = DateTime.Now.AddSeconds((double)kvp.Value.remain - ConnectController.Ping() / 2);
						Log("События: Новое значение оставшегося времени "+ kvp.Key + " "+GetEventRemain(kvp.Key));
					}

					if (kvp.Value.timeout != null)
					{
						ev.timeout = kvp.Value.timeout;
					}


					if (kvp.Value.data != null) 
					{
						ev.data = kvp.Value.data;
					}

					// если false то сервер создал это событие. true по умолчанию 
					if (kvp.Value.from_client != null)
						ev.from_client = kvp.Value.from_client;

					if (kvp.Value.action != null)
					{
						ev.action = kvp.Value.action;

						// если обнулилось событие то и обнуляются данные события (просто не высылаем что бы не тратить время)
						if(kvp.Value.action == "")
                        {
							ev.data = null;
						}
					}
				}
			}

			// Данные применены — двигаем признак изменения (см. Version). В КОНЦЕ и в БАЗОВОМ методе:
			// наследники (EnemyModel, PlayerModel) раскладывают свои компоненты ДО вызова base.SetData,
			// поэтому здесь применённым оказывается уже всё, что привёз пакет.
			Version++;
		}

		/// <summary>
		/// Срок, которым обходится тот, кому считать надо прямо сейчас, а сервер о группе ещё не говорил:
		/// первый шаг до ответа на команду движения, откат кнопки в первый миг после нажатия. Живёт у
		/// потребителей (<see cref="EventTimeout"/>), а не в самой записи: запись обязана отличать
		/// названный сервером срок от неназванного, и подстановка числа прямо в неё это различие стирала.
		/// </summary>
		public const double DEFAULT_TIMEOUT = 0.5;

		/// <summary>
		/// Запись группы, ЗАВОДЯ её при надобности. Зовут те, кто событие ставит: разбор пришедшего пакета
		/// и отправка своей команды (там же проставляется срок её завершения). Читающим сюда нельзя —
		/// вопрос о чужой группе оставил бы у сущности запись о команде, которой у неё не бывает: у
		/// торговца — о ходьбе, у предмета на земле — о лечении. Для чтения есть <see cref="TryGetEvent"/>
		/// и стоящие на нём <see cref="EventTimeout"/>, <see cref="HasEventTimeout"/>,
		/// <see cref="GetEventRemain"/>.
		/// </summary>
		public virtual Event getEvent(string group)
		{
			if (!events.ContainsKey(group))
			{
				events.Add(group, new Event());
				events[group].action = null;
				events[group].from_client = true;
				events[group].finish = DateTime.Now;
			}

			return events[group];
		}

		/// <summary>
		/// Запись группы, если она у сущности есть; иначе null и ничего не заводится.
		/// </summary>
		public Event TryGetEvent(string group)
		{
			Event ev;
			return events.TryGetValue(group, out ev) ? ev : null;
		}

		/// <summary>
		/// Назвал ли сервер срок этой группы для ЭТОЙ сущности. Спрашивают те, кто по сроку СЧИТАЕТ
		/// величину для показа (скорость шага, частота восстановления): пока команда не приходила, срока
		/// нет — свой персонаж до первого шага, только что появившееся существо, — и посчитанное число
		/// выглядело бы настоящим.
		/// </summary>
		public bool HasEventTimeout(string group)
		{
			Event ev = TryGetEvent(group);
			return ev != null && ev.timeout != null;
		}

		/// <summary>
		/// Срок группы для работы самой команды: названный сервером, иначе <see cref="DEFAULT_TIMEOUT"/>.
		/// Ждать названного тут нельзя — команду отправляют и откатывают её кнопку с первого мига, когда
		/// сервер о сроке ещё не говорил.
		/// </summary>
		public double EventTimeout(string group)
		{
			Event ev = TryGetEvent(group);
			return ev != null && ev.timeout != null ? ev.timeout.Value : DEFAULT_TIMEOUT;
		}

		/// <summary>
		/// получения поля data события , нужно указвать какой cnnhernehs данных мы ожидаем будет это поле (по умолчанию это просто объект)
		/// </summary>
		public T getEventData<T>(string group) where T : new()
		{
			Event ev = TryGetEvent(group);
			return ev != null && ev.data != null ? ev.data.ToObject<T>() : new T();
		}

		/// <summary>
		/// вернет количество секунд которых осталось до времени когда событие может быть сработано (тк есть события что шлем мы , а есть что шлются сами). из него уже был вычтено время затраченное на получение пакета с этим значением отсервера на сюда клиент (пол пинга)
		/// если включена интерполяция при отправке команды будет еще вычтено пол пинга (время на доставку пакета команды на сервер ) для проверки можно ли уже слать запрос
		/// Команды у сущности нет вовсе — ждать нечего, ноль: она не идёт, а не «вот-вот завершится».
		/// </summary>
		public virtual double GetEventRemain(string group)
		{
			// тут пинг не выитаем тк для анимации еще используется (она ведь должна продолжаться пока пакет идет).а если отправка команд идет в ConnectController - сверяясь вычитая пол пинга
			Event ev = TryGetEvent(group);
			return ev != null && ev.finish != null ? ev.finish.Value.Subtract(DateTime.Now).TotalSeconds : 0;
		}

		/// <summary>
		/// Включает вывод Log и LogWarning для всех entity.
		/// При false подавляются информационные и предупреждающие сообщения (LogError выводится всегда).
		/// Переключается в runtime: EntityModel.verbose = true/false
		/// </summary>
		public static bool verbose = false;

		// Префикс лог-строк — имя объекта = wire-ключ kind_slug: идентичность (slug/логин) уже в нём,
		// тот же формат у серверных логов (EntityAbstract фреймворка).
		public void Log(string message)
        {
			if (verbose)
				Debug.Log(name + ": "+ message);
		}

		public void LogWarning(string message)
        {
			if (verbose)
				Debug.LogWarning(name + ": "+ message);
		}
		public void LogError(string message)
        {
			Debug.LogError(name + ": "+ message);
		}

		// Universal Animator с одиночным слоем remove (4 state по направлениям) — fallback-эффекты
		// для action'ов, которых нет в SCML конкретного prefab'а. Lazy-load один раз, шарится между сущностями.
		// Подробнее — CLAUDE.md «Архитектура анимаций».
		private static RuntimeAnimatorController _universalController;
		private static bool _universalControllerMissing = false;

		/// <summary>
		/// Lazy-load Universal-контроллера (общий для всех сущностей). null — ассета нет в Resources,
		/// предупреждение выдаётся один раз, эффекты отключены.
		/// </summary>
		private RuntimeAnimatorController GetUniversalController()
		{
			if (_universalControllerMissing) return null;
			if (_universalController == null)
			{
				_universalController = Resources.Load<RuntimeAnimatorController>("Animations/Universal");
				if (_universalController == null)
				{
					_universalControllerMissing = true;
					LogWarning("GetUniversalController: Resources/Animations/Universal не найден — fallback-эффекты отключены");
				}
			}
			return _universalController;
		}

		/// <summary>
		/// direction-параметр Universal.controller из Forward: 0=down, 1=left, 2=right, 3=up.
		/// </summary>
		private int ForwardToDirection()
		{
			return Mathf.Abs(Forward.y) > Mathf.Abs(Forward.x) ? (Forward.y < 0 ? 0 : 3) : (Forward.x < 0 ? 1 : 2);
		}

		/// <summary>
		/// Навешивает на сущность Universal Animator (или меняет controller существующего на Universal).
		/// Вызывается из Spriter-init и image-init.
		///
		/// Параметр <paramref name="startDisabled"/>: если true — сразу выключает Animator (anim.enabled=false).
		/// Это нужно для image-prefab'ов: у них SpriteRenderer.sprite присваивается через TryGetSprite после
		/// этого вызова, и активный Animator перехватывал бы контроль и сбрасывал спрайт (item рендерился
		/// бы как пустой). PlayAction перед запуском Universal-ветки включает Animator обратно.
		/// После привязки вызывает <see cref="OnAnimatorAttached"/>, чтобы наследники обновили кеши.
		/// </summary>
		public void EnsureUniversalAnimator(bool startDisabled = false)
		{
			if (GetUniversalController() == null) return;

			var anim = GetComponent<Animator>();
			if (anim == null) anim = gameObject.AddComponent<Animator>();
			if (anim.runtimeAnimatorController != _universalController)
				anim.runtimeAnimatorController = _universalController;
			if (startDisabled) anim.enabled = false;

			OnAnimatorAttached(anim);
		}

		/// <summary>
		/// Hook для подкласса — позволяет обновить локальные кеши (например, ObjectModel.animator) после
		/// того как сторонний код (Spriter-init / image-init) навесил Animator на GO уже после Awake.
		/// </summary>
		protected virtual void OnAnimatorAttached(Animator anim) { }

		#region Тело сущности

		// Всё, что нужно механике сущности от её тела — есть ли клип, какой играет, повторяется ли он,
		// запустить другой, показать или спрятать, — собрано тут: вызывающие спрашивают тело, не зная,
		// чем оно собрано.

		/// <summary>Скелет Spine сущности; null — тело не собрано (картинка, заглушка либо ещё качается).</summary>
		private SkeletonAnimation Skeleton => GetComponentInChildren<SkeletonAnimation>();

		/// <summary>Собрано ли тело, которым управляют клипами.</summary>
		public bool HasBody => Skeleton != null;

		/// <summary>Есть ли у тела такой клип.</summary>
		public bool BodyHasClip(string clip)
		{
			if (string.IsNullOrEmpty(clip)) return false;

			var skeleton = Skeleton;
			if (skeleton == null) return false;

			var data = skeleton.SkeletonDataAsset != null ? skeleton.SkeletonDataAsset.GetSkeletonData(false) : null;
			return data != null && data.FindAnimation(clip) != null;
		}

		/// <summary>Имя играющего клипа; null — тело ничего не играет.</summary>
		public string BodyClip
		{
			get
			{
				var track = BodyTrack;
				return track != null && track.Animation != null ? track.Animation.Name : null;
			}
		}

		/// <summary>Повторяется ли играющий клип. Клипа нет — считаем неповторяемым: доиграл и стоит.</summary>
		public bool BodyClipLoops
		{
			get
			{
				var track = BodyTrack;
				return track != null && track.Loop;
			}
		}

		/// <summary>
		/// Длительность играющего клипа в секундах; 0 — тело ничего не играет. Ждать конца анимации по ней
		/// и приходится: у разового клипа (гибель, удаление с карты) сам конец наступает молча.
		/// </summary>
		public float BodyClipLength
		{
			get
			{
				var track = BodyTrack;
				return track != null && track.Animation != null ? track.Animation.Duration : 0f;
			}
		}

		/// <summary>Дорожка клипа скелета — та единственная, которой телом и управляют.</summary>
		private Spine.TrackEntry BodyTrack
		{
			get
			{
				var skeleton = Skeleton;
				return skeleton != null && skeleton.AnimationState != null
					? skeleton.AnimationState.GetTrack(0)
					: null;
			}
		}

		/// <summary>
		/// Показывать ли тело. Прячут его на время терминального эффекта поверх сущности (силуэт гибели,
		/// облачко удаления): эффект рисуется на КОРНЕВОМ рендерере, а тело перекрывало бы его. Сам скелет
		/// при этом остаётся живым — выключен только его рисователь, и клип продолжает идти.
		/// </summary>
		private void SetBodyVisible(bool visible)
		{
			var skeleton = Skeleton;
			if (skeleton == null) return;

			var renderer = skeleton.GetComponent<Renderer>();
			if (renderer != null) renderer.enabled = visible;
		}

		/// <summary>
		/// Прозрачность всего, что нарисовано на сущности. Картинка и надетые предметы рисуются спрайтами —
		/// прозрачность у них в цвете каждого рендерера; скелет Spine рисуется мешем, и её несёт сам скелет,
		/// у которого своего спрайта нет вовсе.
		///
		/// Состав рендереров не запоминается: тело пересобирается в любой момент (смена облика, надетый
		/// предмет), и снятый однажды список назавтра красил бы не то.
		/// </summary>
		public void SetBodyAlpha(float alpha)
		{
			foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
			{
				var color = sr.color;
				sr.color = new Color(color.r, color.g, color.b, alpha);
			}

			var skeleton = Skeleton;
			if (skeleton != null && skeleton.Skeleton != null)
			{
				var tint = skeleton.Skeleton.GetColor();
				skeleton.Skeleton.SetColor(tint.r, tint.g, tint.b, alpha);
			}
		}

		/// <summary>
		/// Запустить клип. Повтор задаёт запускающий: у формата Spine это свойство не клипа, а решения —
		/// перечень однократных клипов приходит в пакете скелета (<see cref="SpineCacheService.Loops"/>).
		/// </summary>
		public void BodyPlay(string clip)
		{
			var skeleton = Skeleton;
			if (skeleton == null) return;

			skeleton.AnimationState.SetAnimation(0, clip, SpineCacheService.Loops(skeleton.SkeletonDataAsset, clip));
		}

		#endregion

		/// <summary>
		/// Универсальное проигрывание action-анимации: сначала пробует клип тела сущности (скелета с
		/// сервера), если для текущего prefab+action своего клипа нет — fallback на Universal
		/// Animator (одиночный слой remove + параметры direction:Int и trigger:remove/&lt;action&gt;).
		///
		/// Возвращает true если анимация запущена (телом или Universal); false если ни там, ни там
		/// нет данных под этот action (вызывающая сторона должна выполнить действие без эффекта).
		/// </summary>
		public bool PlayAction(string actionName)
		{
			// 1) собственный клип тела — приоритет
			if (HasBody && !string.IsNullOrEmpty(prefab))
			{
				var (clip, _, clipAngle) = AnimationCacheService.GetClipName(
					prefab, actionName, Forward.x, Forward.y);
				if (BodyHasClip(clip))
				{
					// Тело снова владеет визуалом — снять терминальный Universal-оверлей (dead-силуэт),
					// если он был активен: вернуть тело, погасить силуэт корневого SR.
					RestoreBodyFromTerminalOverlay();
					BodyPlay(clip);
					DisplayAngle = clipAngle;
					return true;
				}
			}

			// 2) Universal Animator — fallback. Только если controller имеет одноимённый Trigger-параметр
			// (иначе SetTrigger спамит "Parameter does not exist"). Список параметров Universal —
			// remove, dead, ... (расширяется по мере добавления универсальных эффектов).
			var unityAnim = GetComponent<Animator>();
			if (unityAnim != null && unityAnim.runtimeAnimatorController != null)
			{
				bool hasTrigger = false;
				foreach (var p in unityAnim.parameters)
					if (p.type == AnimatorControllerParameterType.Trigger && p.name == actionName) { hasTrigger = true; break; }
				if (!hasTrigger)
				{
					// Под этот action нет ни клипа тела, ни Universal-триггера — «обычное» состояние
					// (walk/idle/resurrect). Если ранее проиграл терминальный Universal-эффект (dead-силуэт)
					// и перезаписал корневой SR — восстанавливаем нормальный визуал:
					//  - kind-only (есть _fallbackSprite): возвращаем placeholder на корневой SR;
					//  - скелет (тело в детях): гасим силуэт корневого SR и показываем тело обратно.
					// Без этого ожившая после dead сущность застревает на dead-кадре (направленных клипов
					// у GetClipName часто NULL → сюда же попадают её walk/idle/resurrect).
					if (_fallbackSprite != null)
					{
						var sr0 = GetComponent<SpriteRenderer>();
						if (sr0 != null && sr0.sprite != _fallbackSprite)
						{
							unityAnim.enabled = false; // снять удержание dead-кадра Animator'ом (writeDefaults=false)
							sr0.sprite = _fallbackSprite;
							sr0.enabled = true;
						}
					}
					else
					{
						RestoreBodyFromTerminalOverlay();
					}
					return false;
				}

				// Image-prefab'ы держат Animator выключенным после init — иначе он перехватывает SR.sprite
				// и item-объекты рендерятся пустыми. Включаем здесь, перед SetTrigger.
				if (!unityAnim.enabled) unityAnim.enabled = true;

				// Universal.anim бьёт PPtrCurve по m_Sprite корневого SpriteRenderer — у сущности со скелетом
				// корневой SR выключен (UpdateController.ApplyVisualPrefab). Включаем на время эффекта, а тело
				// прячем — иначе Puff-кадры перекрываются им. Сам скелет живой: выключен только его
				// рисователь, клип продолжает идти.
				var sr = GetComponent<SpriteRenderer>();
				if (sr != null) sr.enabled = true;
				SetBodyVisible(false);

				unityAnim.SetInteger("direction", ForwardToDirection());
				unityAnim.ResetTrigger(actionName);
				unityAnim.SetTrigger(actionName);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Возврат визуала тела после терминального Universal-оверлея (dead-силуэт / remove-Puff).
		/// Оверлей (Universal-ветка PlayAction) зажигает корневой SR (силуэт) и ПРЯЧЕТ тело; сам оверлей
		/// назад его не возвращает — оживший после dead застревал бы на dead-кадре (корневой SR держит
		/// силуэт при writeDefaults=false), даже когда его walk/idle-клип уже играется на скрытом теле.
		/// Зовём, когда тело снова получает управление (не-эффектный action). no-op, если оверлея нет:
		/// у сущности со скелетом корневой SR штатно выключен, включён — значит его зажёг оверлей.
		/// </summary>
		private void RestoreBodyFromTerminalOverlay()
		{
			// Тела нет — корневой SR рисует сам визуал сущности (картинку предмета, заглушку вида), и гасить
			// его нельзя: это не силуэт оверлея.
			if (!HasBody) return;

			var sr = GetComponent<SpriteRenderer>();
			if (sr == null || !sr.enabled) return;   // оверлея нет — восстанавливать нечего

			sr.enabled = false;   // погасить силуэт корневого SR (тело рисует дочерний скелет)
			SetBodyVisible(true);
		}

		/// <summary>
		/// Суммарные world-границы ВИДИМОГО тела сущности (image / fallback / Universal-силуэт — один
		/// корневой рендерер; скелет Spine рисует мешем; надетые предметы — свои). Выключенные
		/// рендереры и спрайты без картинки не учитываем.
		/// Единый источник «тела» сущности для клиентских гейтов, чтобы они совпадали: кольцо-подсветка
		/// трупа (CursorController) и подгонка его кликабельного коллайдера (ObjectModel) берут ОДНИ И ТЕ ЖЕ
		/// границы — область клика по трупу совпадает с кольцом.
		/// </summary>
		public bool TryGetVisualBounds(out Bounds bounds)
		{
			bounds = new Bounds();
			bool has = false;
			var renderers = GetComponentsInChildren<Renderer>();
			for (int i = 0; i < renderers.Length; i++)
			{
				if (!renderers[i].enabled) continue;
				// Пустой спрайт рисует ничто, а границы у рендерера всё равно есть — такой в тело не идёт.
				if (renderers[i] is SpriteRenderer sprite && sprite.sprite == null) continue;
				if (!has) { bounds = renderers[i].bounds; has = true; }
				else bounds.Encapsulate(renderers[i].bounds);
			}
			return has;
		}

		/// <summary>
		///  базовая корутина уничтожение с карты объекта при уничтожении с сервера. ее можно и скорее нужно переопределять насыщая анмиацией это действи
		/// </summary>
		public virtual IEnumerator Remove(bool isChangeMap = false)
		{
			if (isChangeMap)
			{
				Log("Удаление - Отложенное удаление при смене карты");

				DateTime start = DateTime.Now.AddSeconds(5);
				while (DateTime.Compare(start, DateTime.Now) >= 1)
				{
					if (action != ConnectController.ACTION_REMOVE)
					{
						Log("Удаление - Существо сменило статус с удаляемого на " + action + ", удаление отменено");
						yield break;
					}
					yield return new WaitForFixedUpdate();
				}
				Log("Удаление - Существо так и не перешло на новую карту");
			}
			StartCoroutine("Destroy");
			yield break;
		}	
		
		protected virtual IEnumerator Destroy()
		{
			Log("Удаление - немедленное удаления с карты");
			Destroy(gameObject);

			yield break;
		}

	}
}
