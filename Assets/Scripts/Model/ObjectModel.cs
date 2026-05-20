using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Collider2D обязателен исключительно для mouse-picking цели.
	///
	/// Как это работает:
	///   CursorController.Update() по клику делает Physics2D.Raycast из Camera.main в мировую точку курсора;
	///   первый hit с GameObject'а, у которого есть EntityModel → это и есть выбранная цель (persist_target = true).
	///   Без Collider2D raycast ничего не увидит и нельзя будет кликать по персонажам/объектам.
	///
	/// Почему НЕ физика:
	///   столкновения/движение авторитарны на сервере, клиент только получает позиции пакетами
	///   (см. ObjectModel.Walk корутину). Коллайдер в физ-симуляции не участвует — поэтому Rigidbody2D
	///   рядом ставим как Kinematic (не Dynamic): его задача — просто подсказать Physics2D, что collider
	///   двигается, чтобы движок не перестраивал spatial index статических collider'ов каждый кадр.
	///
	/// Размер/offset капсулы тюнится под визуал конкретной сущности; при рефакторинге не забыть,
	/// что wrap-ом SpriteRenderer'а в child "Sprites" (UpdateController.UpdateObject) меняется только
	/// визуальный scale, а Collider2D остаётся на корне со своими префабными размерами.
	/// </summary>
	[RequireComponent(typeof(Collider2D))]
	public class ObjectModel : EntityModel
	{
		[NonSerialized]
		public Animator animator;

		/// <summary>
		/// список анимационных тригеров
		/// </summary>
		private static Dictionary<string, bool> trigers;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		private int _layerIndex = 0;

		// Кешированное наличие параметров x/y в текущем animator.runtimeAnimatorController.
		// Forward setter ниже использует SetFloat("x"/"y") — это валидно только для blend-tree
		// контроллеров (старый PlayerController). Universal.controller на enemy/object этих параметров
		// не имеет; обращение без guard'а спамит "Parameter 'x' does not exist" каждый кадр.
		private bool _hasParamX;
		private bool _hasParamY;


		public int CurrentAnimationIndex
		{
			get { return _layerIndex; }
		}


		private Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

		/// <summary>
		///  это сторона движения игркоа. как transform forward ,  автоматом нормализует значения
		/// </summary>
		public override Vector3 Forward
		{
			get { return base.Forward; }
			set
			{
				// вообще сервер сам нормализует но так уменьшиться пакет размера символов
				if (value.x != base.Forward.x || value.y != base.Forward.y)
				{
					// Blend-tree аниматора (legacy PlayerController использует параметры x/y для смешивания
					// направлений). Универсальный Animator с Universal.controller их не имеет —
					// guard через закешированный _hasParamX/Y (заполняется в RebuildTriggersCache).
					if (animator)
					{
						if (_hasParamX && animator.GetFloat("x") != value.x)
							animator.SetFloat("x", value.x);
						if (_hasParamY && animator.GetFloat("y") != value.y)
							animator.SetFloat("y", value.y);
					}
					base.Forward = value;
				}
			}
		}

		protected virtual void Awake()
		{
			if (animator = GetComponent<Animator>())
				RebuildTriggersCache();
		}

		/// <summary>
		/// Spriter-init / image-init навешивают Animator на GO после Awake — кеш этого момента ещё null.
		/// Hook обновляет ObjectModel.animator и пересобирает trigers под новый controller.
		/// </summary>
		protected override void OnAnimatorAttached(Animator anim)
		{
			animator = anim;
			RebuildTriggersCache();
		}

		private void RebuildTriggersCache()
		{
			_hasParamX = false;
			_hasParamY = false;
			if (animator == null) return;
			trigers ??= new Dictionary<string, bool>();
			foreach (var parameter in animator.parameters)
			{
				if (parameter.type == AnimatorControllerParameterType.Trigger && !trigers.ContainsKey(parameter.name))
					trigers.Add(parameter.name, true);
				if (parameter.name == "x") _hasParamX = true;
				else if (parameter.name == "y") _hasParamY = true;
			}
		}

		// Update is called once per frame
		void Update()
		{
			// если текущий наш статус анимации - не стояние и давно небыло активности - включим анмацию остановки.
			// Имя idle-action берётся из ConnectController.idle_action (серверное, default "idle") — не хардкодим,
			// чтобы одна переменная управляла обеими ветками (Spriter adjuster + legacy Animator).
			// Ограничение: layer в Animator Controller должен называться так же, как ConnectController.idle_action —
			// обычно "idle", но если сервер переименует, .controller'ы тоже надо будет переименовать.
			string idleAction = ConnectController.idle_action;
			if (action == "dead" || action == ConnectController.ACTION_REMOVE) return;
			if (DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) >= 1) return;

			// 1) Legacy multi-layer Animator (например PlayerController.controller со слоями по action).
			if (animator != null
				&& (animator.GetCurrentAnimatorStateInfo(_layerIndex).loop
					|| animator.GetCurrentAnimatorStateInfo(_layerIndex).normalizedTime >= 1.0f))
			{
				string layer_name = animator.GetLayerName(_layerIndex);
				if (layer_name != idleAction)
				{
					int idx = animator.GetLayerIndex(idleAction);
					if (idx >= 0)
					{
						Log("Анимация " + key + " с " + action + " на " + idleAction + " (таймаут)");
						Animate(animator, idx);
					}
				}
			}

			// 2) Spriter (SCML): таймаут с последнего action-пакета от сервера — переключаем на idle.
			// Проверку cur.Looping/Progress намеренно не делаем: SCML-контент часто помечает action'ы
			// (Attack, Hurt) как Looping=true, и они зацикливаются вечно. Триггер для возврата в idle
			// — только activeLast timeout (как у legacy multi-layer Animator выше).
			var spriter = GetComponent<SpriterDotNetUnity.SpriterDotNetBehaviour>();
			var cur = spriter?.Animator?.CurrentAnimation;
			if (cur != null && !string.IsNullOrEmpty(prefab)
				&& AnimationCacheService.GetClipNameSimple(prefab, idleAction, ConnectController.entity_actions) is string idleClip
				&& spriter.Animator.HasAnimation(idleClip)
				&& cur.Name != idleClip)
			{
				Log("Spriter: " + key + " с " + cur.Name + " на " + idleClip + " (таймаут)");
				PlayAction(idleAction);
			}
		}

		/// <summary>
		/// этот метод для возможноости переопределения его же самого нужен но с другими типами аргументов
		/// </summary>
		public override void SetData(EntityRecive recive)
		{
			this.SetData((ObjectRecive)recive);
		}

		/// <summary>
		/// переопределим метод срабатываемый при присвоениеии пришедших с сервера данных и начнем включать анимацию
		/// </summary>
		protected void SetData(ObjectRecive recive)
		{
			Vector3 old_position = position;
			int old_map_id = map;

			base.SetData(recive);

			// при первой загрузке не запускаем
			if ((recive.x != null || recive.y != null || recive.z != null) && old_position != position)
			{
				Vector3 new_position = new Vector3(recive.x ?? old_position.x, recive.y ?? old_position.y, recive.z ?? old_position.z);

				// если первый вход в игру
				if (old_position == Vector3.zero) 
					transform.localPosition = new_position;
				else
				{            
					Log("Движение - новые данные с сервера о переходе с "+ old_position + " на "+ new_position+" существа в локальной позиции "+transform.localPosition);
					if (coroutines.ContainsKey("walk"))
					{
						LogWarning("Движение - существо еще не звершило движение. Эстраполяция: " + Math.Round((Vector3.Distance(transform.localPosition, old_position) / Vector3.Distance(old_position, new_position)) * 100) + " % не дойдя с прошлого движения");
					}

					// Walk запускаем в двух случаях:
					// 1) walk-шаг: новые координаты близки к продлению текущего шага (old + Forward*step);
					// 2) смена карты: после SetParent(worldPositionStays=true) localPosition пересчитан в систему
					//    новой карты, new_position — серверная в той же системе → Walk плавно догонит. Подменять
					//    new_position на (old + Forward*step) НЕЛЬЗЯ: old_position был в системе СТАРОЙ карты,
					//    подмена ломает серверные координаты и игрок ехал не туда.
					if ((recive.action == "walk" && Vector3.Distance(old_position + (Forward * ConnectController.step), new_position) < ConnectController.step * 0.5f) || (recive.map != null && recive.map != old_map_id))
					{
						// в приоритете getEvent(WalkResponse.GROUP).timeout  тк мы у него не отнимаем время пинга на получение пакета но и не прибавляем ping время на отправку с сервера нового пакета
						coroutines["walk"] = StartCoroutine(Walk(new_position, (coroutines.ContainsKey("walk") ? coroutines["walk"] : null)));
					}
					else
					{
						if (coroutines.ContainsKey("walk"))
						{
							Log("Движение - остановка корутины");

							// по каким то причинам бывает запись есть и выдает ошибку NullReferenceException: routine is null
							if(coroutines["walk"]!=null)
								StopCoroutine(coroutines["walk"]);

							coroutines.Remove("walk");
						}

						// выстрелы могут телепортироваться в конце что бы их взрыв был на клетке существа а негде то около рядом
						Log("Движение -телепорт из " + transform.localPosition + " в " + new_position+" ("+Vector3.Distance(transform.localPosition, new_position) +")");
						transform.localPosition = new_position;
					}
				}
			}

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			// todo некоторые анимации не нужно запускать если существо только добавлено (например смерти тк умерло оно может уже давно а карта только загрузилась)
			if (recive.action != null && recive.action != ConnectController.ACTION_REMOVE)
			{
				// 1) legacy multi-layer controller (PlayerController.controller): слой по имени action.
				// 2) Universal-overlay (1 слой + триггеры): нет одноимённого layer'а → PlayAction
				//    сам решит Spriter→Universal trigger по имени action.
				int layerIndex = animator != null ? animator.GetLayerIndex(recive.action) : -1;
				if (layerIndex >= 0)
					Animate(animator, layerIndex);
				else
					PlayAction(recive.action);
			}
		}

		/// <summary>
		/// включить анимацию - те отключить все слои анимаций других и оставить только нужную. если есть анмиационный тригер одноименный со слоем - и его выключить (для анимаций которых не зацикленные и надо запустить один раз)
		/// </summary>
		public void Animate(Animator animator, int layerIndex)
		{		
			if (layerIndex >=0)
			{
				if (layerIndex == 0 || animator.GetLayerWeight(layerIndex) != 1) 
				{ 
					// "остановим" все слои анмиации
					if (animator.layerCount > 1) 
					{ 
						for (int i = 1; i < animator.layerCount; i++)
						{
							animator.SetLayerWeight(i, 0);
						}
					}
					animator.SetLayerWeight(layerIndex, 1);		
				}
				this._layerIndex = layerIndex;

				string name = animator.GetLayerName(layerIndex);
				if (trigers.ContainsKey(name))
				{
					Log("запускаем тригер " + name);
					animator.SetTrigger(name);
				}
			}
			else
				PlayerController.Error("неверный индекс анимации "+ layerIndex);
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// она вошла в плагин тк движение нужно в любой игре а координаты часть стандартного функционала, вы можете переопределить ее
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		protected virtual IEnumerator Walk(Vector3 finish, Coroutine old_coroutine)
		{
			if (old_coroutine != null)
			{
				Log("Движение - Остановка старой корутины с запуском новой");
				StopCoroutine(old_coroutine);
			}
			else
				Log("Движение - новая корутина");

			if (finish == transform.localPosition)
			{
				LogError("Движение - позиция к которой движемся равна той на которой стоим");
				coroutines.Remove("walk");
				yield break;
			}

			float distance;

			double timeout = (1.0 / ConnectController.server_fps);					   // если существо переходит на другую карту то пакет придет с картой в следующем кадре сервера
			timeout += ConnectController.Ping();                                       // время с который одна локация передаст другой локации пакет с существом или игроком
			timeout += Time.fixedDeltaTime;											   // добавляем 1 кадра тк пакет с новыми координатами может прийти во время во врмеся пауз между кадрами FixedUpdate

			// если мы уходим с карты надо замедлиться на время полных пинга
			// мы не првоеряем удаляется ли существо или именно переходит (в обоих случаях action одинаков, но при переходе новая карта указывается) тк при удалении окончательном эта корутина уничтожается с существом
			if (action == ConnectController.ACTION_REMOVE)
            {
				// если расчетное время получения пакета меньше чем обычно анимация шага у персонажа - делаем время анимации шага персонада
				if (timeout < getEvent(WalkResponse.GROUP).timeout)
					timeout = (double)getEvent(WalkResponse.GROUP).timeout;
			}
			//мы не знаем будет ли существо идти дальше (новый пакет с запазданием придет после завершения текущего движения даже если пришлел ровно к нему)
			//это времени для возврата с сервера нам результата назад уже следующего события движения
			//и раз мы не знаем наверняка будет ли существо идти дальше всегда поедполагаем что ДА (там не сильно далеко уйдем даже если НЕТ)
			else
			{
				// отрезок пути которой существо движется за кадр
				timeout += GetEventRemain(WalkResponse.GROUP);
			}

			// Постоянная скорость анимации = STEP / timeout (а не actualDistance / timeout).
			// Иначе мелкие шаги (slide вдоль стены, corner wrap) играются медленно и видны
			// как "замедления" между нормальными шагами. С STEP в формуле скорость стабильна:
			// мелкий шаг доходит до finish раньше срока, дальше idle wait до нового пакета.
			double distancePerUpdate = (ConnectController.step / (timeout / Time.fixedDeltaTime));
			bool extrapolation = false;
			// время начала экстраполяции для ограничения по MaxPing * 2
			DateTime extrapolationStart = DateTime.MinValue;

			while (true)
			{
				if (action != "walk" && action != ConnectController.ACTION_REMOVE)
				{
					LogWarning("Движение - Сменен action во время движения на " + action+", удаляем корутину");
					transform.localPosition = finish;
					break;
				}

				distance = Vector3.Distance(transform.localPosition, finish);

				// если уже подошли но с сервера пришла инфа что следом будет это же событие группы - экстрополируем движение дальше
				if (distance < distancePerUpdate)
				{
					// если ожидается пакет на движение или мы удаляемся — экстраполируем на полный шаг с замедлением
					if ((getEvent(WalkResponse.GROUP).action!=null && getEvent(WalkResponse.GROUP).action.Length>0 || action == ConnectController.ACTION_REMOVE) && !extrapolation)
					{
						extrapolation = true;
						extrapolationStart = DateTime.Now;

						Vector3 nextFinish = finish + Forward * ConnectController.step;
						int ntx = Mathf.RoundToInt(nextFinish.x);
						int nty = Mathf.RoundToInt(nextFinish.y);

						if (MapDecodeModel.Colliders.Contains(new Vector2Int(ntx, nty)))
						{
							// Не экстраполируем в коллайдер. Snap к (tx±0.49) делал телепорт когда
							// текущая позиция уже не целая (после серверного creep или диагонали).
							// Сервер сам подводит игрока к стене через creep в walk/index.php.
							LogWarning("Движение - следующий тайл коллайдер, останавливаемся на серверной позиции");
							break;
						}
						else
						{
							finish = nextFinish;
							distancePerUpdate *= 0.7;
							LogWarning("Движение - экстраполируем на полный шаг, замедление 0.7x");
						}
					}
                    else
					{
						// проверяем лимит времени экстраполяции — не ждать дольше MaxPing * 2
						if (extrapolation && DateTime.Compare(extrapolationStart.AddSeconds(ConnectController.MaxPing() * 2), DateTime.Now) < 1)
						{
							LogWarning("Движение - лимит времени экстраполяции, останавливаемся");
							break;
						}

                        // если экстраполировали расстоянием то остаемся в тех координатах куда мы прошли чуть больше, что бы не отбрасывало назад (на координаты сервера)
                        if (!extrapolation)
                        {
							LogWarning("Движение - дошли, но телепортируеся на "+ Vector3.Distance(transform.localPosition, finish)+" до конечной точки");
							transform.localPosition = finish;
						}
						else
							LogWarning("Движение - дошли и была экстраполяция расстоянием");

						break;
					}
				}

				activeLast = DateTime.Now;
				transform.localPosition = Vector3.MoveTowards(transform.localPosition, finish, (float)distancePerUpdate);

				Log("Движение - перешли в "+ transform.localPosition +", осталось время "+ GetEventRemain(WalkResponse.GROUP)+" сек., расстояние "+ distance);

				yield return new WaitForFixedUpdate();
			}
			coroutines.Remove("walk");

			Log("Движение - завершена корутина движения");
			yield break;
		}

		/// <summary>
		/// анимированное удаление объекта с карты (когда снаряд попал в цель или игрок уходит с карты).
		/// Пытается проиграть ACTION_REMOVE через PlayAction (Spriter→Universal Animator fallback).
		/// Если ни в SCML, ни в Universal.controller нет данных — удаление мгновенное.
		/// </summary>
		protected override IEnumerator Destroy()
		{
			if (PlayAction(ConnectController.ACTION_REMOVE))
			{
				Log("Удаление - Запуск анимации удаления с карты");

				// Ждём один кадр чтобы Animator/Spriter переключились на нужный state,
				// затем берём длину текущего state'а.
				yield return null;

				var anim = GetComponent<Animator>();
				if (anim != null && anim.runtimeAnimatorController != null)
				{
					var info = anim.GetCurrentAnimatorStateInfo(0);
					if (info.length > 0.01f)
						yield return new WaitForSeconds(info.length - 0.01f);
				}
				else
				{
					var spriter = GetComponent<SpriterDotNetUnity.SpriterDotNetBehaviour>();
					if (spriter?.Animator?.CurrentAnimation != null)
						yield return new WaitForSeconds(spriter.Animator.CurrentAnimation.Length / 1000f - 0.01f);
				}
			}

			Log("Удаление - немедленное удаления с карты");
			Destroy(gameObject);

			yield break;
		}
	}
}
