using SpriterDotNetUnity;
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
		public string login;

		/// <summary>
		/// стандартное поле действия. хорошим тоном связать его с анимацией и в серверных механиках использовать
		/// </summary>
		[NonSerialized]
		public string action = "idle";

		[NonSerialized]
		public string prefab;

		private Vector3 _forward = Vector3.zero;

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
					// Берём аниматор именно этой сущности, а не первый найденный в сцене
					SpriterDotNetBehaviour animator = GetComponent<SpriterDotNetBehaviour>();
					if (animator != null && animator.Animator != null)
					{
						string prefabName = !string.IsNullOrEmpty(recive.prefab) ? recive.prefab : this.prefab;
						float fwdX = recive.forwardX ?? Forward.x;
						float fwdY = recive.forwardY ?? Forward.y;
						var (clipName, flipX) = AnimationCacheService.GetClipName(
							prefabName, recive.action, fwdX, fwdY, ConnectController.entity_actions);
						if (clipName == null) { clipName = recive.action; flipX = false; }

						// Action не имеет клипа в SCML (ACTION_LOAD, не настроенный action и т.п.) —
						// fallback на idle_action, иначе SpriterDotNet оставит первую анимацию SCML
						// (которая может быть какой угодно — у player'а это Attack).
						if (!animator.Animator.HasAnimation(clipName))
						{
							var (idleClip, idleFlip) = AnimationCacheService.GetClipName(
								prefabName, ConnectController.idle_action, fwdX, fwdY, ConnectController.entity_actions);
							if (idleClip != null && animator.Animator.HasAnimation(idleClip))
							{
								clipName = idleClip;
								flipX = idleFlip;
							}
						}

						if (type != "object")
						{
							Vector3 s = transform.localScale;
							transform.localScale = new Vector3(
								flipX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
						}

						bool changed = action != recive.action;
						bool nonLoop = animator.Animator.CurrentAnimation != null && !animator.Animator.CurrentAnimation.Looping;
						bool animationDiverged = animator.Animator.CurrentAnimation == null
							|| animator.Animator.CurrentAnimation.Name != clipName;
						if (changed || nonLoop || animationDiverged)
						{
							if (animator.Animator.HasAnimation(clipName))
								animator.Animator.Play(clipName);
							else
								LogWarning("Анимация: clip '" + clipName + "' (action '" + recive.action + "') не найден в SCML");
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
					SpriterDotNetBehaviour anim = GetComponent<SpriterDotNetBehaviour>();
						
				// Forward сменился без смены action — ре-резолв направленного clip
					string pn = this.prefab;
					if (anim?.Animator != null && !string.IsNullOrEmpty(pn))
					{	
						if (anim && action != null && recive.action == null)
						{
							var (newClip, newFlip) = AnimationCacheService.GetClipName(
								pn, action, Forward.x, Forward.y, ConnectController.entity_actions);
							if (newClip != null)
							{
								Vector3 s = transform.localScale;
								transform.localScale = new Vector3(
									newFlip ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
								if (anim.Animator.CurrentAnimation == null || anim.Animator.CurrentAnimation.Name != newClip)
								{
									if (anim.Animator.HasAnimation(newClip))
										anim.Animator.Play(newClip);
								}
							}
						}
					}
					// Поворот transform применяем только сущностям без Spriter и без legacy blend-tree Animator'а.
					// У player/enemy/animal направление передаётся сменой clip + flip по X (Spriter), либо
					// SetFloat("x"/"y") в blend-tree (legacy PlayerController.controller) — им крутить
					// transform нельзя.
					// Universal.controller (overlay для remove-эффектов) не имеет параметров x/y — для image-
					// projectile'ов с ним крутить ВСЁ ЕЩЁ можно.
					// Критерий «нельзя крутить»: есть Spriter, или есть Animator с параметрами x/y.
					// Конвенция: спрайт нарисован вправо (→). Atan2(y,x) — угол от оси X+. Server default
					// forward=(0,-1) → angle=-90° → спрайт смотрит вниз.
					else if (GetComponent<SpriterDotNetBehaviour>() == null)
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
							transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg);
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

			if (recive.login != null)
				this.login = recive.login;

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
		}

		/// <summary>
		/// получение данных события (без поля data)
		/// </summary>
		public virtual Event getEvent(string group)
		{
			if (!events.ContainsKey(group))
			{
				events.Add(group, new Event());
				events[group].action = null;
				events[group].timeout = 0.5;
				events[group].from_client = true;
				events[group].finish = DateTime.Now;
			}

			return events[group];
		}

		/// <summary>
		/// получения поля data события , нужно указвать какой cnnhernehs данных мы ожидаем будет это поле (по умолчанию это просто объект)
		/// </summary>
		public T getEventData<T>(string group) where T : new()
		{
			EventRecive ev = getEvent(group);
			return ev.data != null ? ev.data.ToObject<T>() : new T();
		}

		/// <summary>
		/// вернет количество секунд которых осталось до времени когда событие может быть сработано (тк есть события что шлем мы , а есть что шлются сами). из него уже был вычтено время затраченное на получение пакета с этим значением отсервера на сюда клиент (пол пинга) 
		/// если включена интерполяция при отправке команды будет еще вычтено пол пинга (время на доставку пакета команды на сервер ) для проверки можно ли уже слать запрос
		/// </summary>
		public virtual double GetEventRemain(string group)
		{
			// тут пинг не выитаем тк для анимации еще используется (она ведь должна продолжаться пока пакет идет).а если отправка команд идет в ConnectController - сверяясь вычитая пол пинга 
			return ((DateTime)getEvent(group).finish).Subtract(DateTime.Now).TotalSeconds;
		}

		/// <summary>
		/// Включает вывод Log и LogWarning для всех entity.
		/// При false подавляются информационные и предупреждающие сообщения (LogError выводится всегда).
		/// Переключается в runtime: EntityModel.verbose = true/false
		/// </summary>
		public static bool verbose = false;

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
			if (_universalControllerMissing) return;
			if (_universalController == null)
			{
				_universalController = Resources.Load<RuntimeAnimatorController>("Animations/Universal");
				if (_universalController == null)
				{
					_universalControllerMissing = true;
					LogWarning("EnsureUniversalAnimator: Resources/Animations/Universal не найден — fallback-эффекты отключены");
					return;
				}
			}

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

		/// <summary>
		/// Универсальное проигрывание action-анимации: сначала пробует Spriter (SCML с сервера), если
		/// для текущего prefab+action нет SCML-клипа — fallback на Universal Animator (одиночный слой
		/// remove + параметры direction:Int и trigger:remove/&lt;action&gt;).
		///
		/// Возвращает true если анимация запущена (Spriter или Universal); false если ни там, ни там
		/// нет данных под этот action (вызывающая сторона должна выполнить действие без эффекта).
		/// </summary>
		public bool PlayAction(string actionName)
		{
			// 1) Spriter — приоритет
			var spriter = GetComponent<SpriterDotNetBehaviour>();
			if (spriter != null && spriter.Animator != null && !string.IsNullOrEmpty(prefab))
			{
				var (clip, _) = AnimationCacheService.GetClipName(
					prefab, actionName, Forward.x, Forward.y, ConnectController.entity_actions);
				if (!string.IsNullOrEmpty(clip) && spriter.Animator.HasAnimation(clip))
				{
					spriter.Animator.Play(clip);
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
				if (!hasTrigger) return false;

				// Image-prefab'ы держат Animator выключенным после init — иначе он перехватывает SR.sprite
				// и item-объекты рендерятся пустыми. Включаем здесь, перед SetTrigger.
				if (!unityAnim.enabled) unityAnim.enabled = true;

				// Universal.anim бьёт PPtrCurve по m_Sprite корневого SpriteRenderer — после Spriter-init
				// корневой SR выключен (см. NewSpriterRuntimeImporter). Включаем на время эффекта.
				// Spriter-children (если есть) глушим — иначе Puff-кадры перекрываются телом Spriter.
				// На детях Spriter'а живут SpriteRenderer'ы body-parts — выключаем их, корневой SR
				// пропускаем (там Universal рисует Puff). SpriterDotNetBehaviour не трогаем — без
				// активных дочерних SR его scheduling кадров не отрендерится.
				var sr = GetComponent<SpriteRenderer>();
				if (sr != null) sr.enabled = true;
				if (spriter != null)
					foreach (var r in spriter.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
						if (r.gameObject != spriter.gameObject) r.enabled = false;

				// direction по Forward: 0=down, 1=left, 2=right, 3=up
				int direction = Mathf.Abs(Forward.y) > Mathf.Abs(Forward.x) ? (Forward.y < 0 ? 0 : 3) : (Forward.x < 0 ? 1 : 2);
				unityAnim.SetInteger("direction", direction);
				unityAnim.ResetTrigger(actionName);
				unityAnim.SetTrigger(actionName);
				return true;
			}

			return false;
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
