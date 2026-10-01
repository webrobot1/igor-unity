using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
	/// Размер/offset капсулы тюнится под визуал конкретной сущности; при рефакторинге не забыть, что
	/// размер ТЕЛА задаётся мимо капсулы — у скелета масштабом его дочернего объекта
	/// (VisualBuilder.Fit), у картинки масштабом корня, — а мировой размер капсулы держится
	/// префабным: масштаб корня ей компенсируют встречно (UpdateController.ApplyVisualPrefab).
	/// </summary>
	[RequireComponent(typeof(Collider2D))]
	public class ObjectModel : EntityModel
	{
		[NonSerialized]
		public Animator animator;

		private Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

		// Зазор кликабельного коллайдера трупа над границами его спрайта. ЧУТЬ больше зазора кольца-подсветки
		// (CursorController.HIGHLIGHT_GAP) — кольцо лежит ВНУТРИ хит-области, клик по кольцу всегда попадает по трупу.
		private const float CORPSE_HIT_GAP = 1.2f;

		// Скорость полёта подобранной вещи к тому, кто её взял, в клетках за секунду: время полёта считается от
		// пройденного расстояния, а не задаётся готовым. Заданное время само подстраивало бы скорость под дальность —
		// вещь с соседней клетки ползла бы, а взятая издалека (телекинез) прыгала бы мгновенно.
		private const float FlightSpeed = 12f;

		// Потолок времени полёта: он объясняет игроку, КУДА делась вещь, и не задерживает её исчезновение
		// настолько, чтобы её успели счесть всё ещё лежащей.
		private const float FlightMax = 0.4f;

		// Полёт короче этого времени глазу неотличим от мгновенного исчезновения — вещь взяли под ногами.
		private const float FlightMin = 0.02f;

		private CapsuleCollider2D _corpseCapsule;
		private Vector2 _liveCapsuleSize;
		private Vector2 _liveCapsuleOffset;
		private bool _liveCapsuleSaved;
		private bool _capsuleFittedToCorpse;
		private Bounds _corpseFittedTo;   // границы тела, под которые капсула подогнана сейчас
		private DeathTimer _deathTimer;

		protected virtual void Awake()
		{
			animator = GetComponent<Animator>();
		}

		/// <summary>
		/// Universal Animator вешает сборка визуала (картинка либо заглушка вида) уже после Awake — кеш
		/// этого момента ещё null. Hook обновляет ObjectModel.animator под навешенный Animator.
		/// </summary>
		protected override void OnAnimatorAttached(Animator anim)
		{
			animator = anim;
		}

		// Update is called once per frame
		void Update()
		{
			// Труп (action=dead) — контейнер лута: подгоняем кликабельный коллайдер под ВИДИМОЕ тело трупа,
			// чтобы клик открывал лут по всему телу (совпадая с кольцом-подсветкой CursorController), а не по
			// узкому пересечению боевой капсулы с иначе расположенной/размерной позой трупа. Ожил/задвигался —
			// возвращаем боевую капсулу. Здесь (Update), а не в LateUpdate: у PlayerModel свой LateUpdate,
			// добавление второго в ObjectModel затенило бы его (потеря вклада — skill code
			// «Переопределение с вкладом родителя»).
			if (action == "dead")
			{
				FitCorpseCollider();

				// Отсчёт срока над телом навешиваем по действию, а не по составу компонентов: срок есть и
				// у тела без добычи, и у игрока. Компонент сам решает, показывать ли (см. DeathTimer).
				if (_deathTimer == null) _deathTimer = gameObject.AddComponent<DeathTimer>();
			}
			else RestoreLiveCollider();

			// если текущий наш статус анимации - не стояние и давно небыло активности - включим анмацию остановки.
			// Имя idle-action берётся из ConnectController.idle_action (серверное, default "idle") — не хардкодим.
			string idleAction = ConnectController.idle_action;
			if (action == "dead" || action == ConnectController.ACTION_REMOVE) return;
			if (DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) >= 1) return;

			// Таймаут с последнего action-пакета от сервера — переключаем тело на idle. Повтор играющего
			// клипа намеренно не спрашиваем: контент часто помечает action'ы (Attack, Hurt) повторяемыми,
			// и они зацикливаются вечно. Триггер для возврата в idle — только activeLast timeout.
			// Резолв клипа для сравнения — направленный (GetClipName по Forward), строго ТОТ ЖЕ, что
			// внутри PlayAction: ненаправленный резолв здесь даёт другой угловой вариант клипа, сравнение
			// с текущим вечно «не совпадает», и Play рестартует анимацию каждый кадр — idle замирает
			// на первом кадре.
			string cur = BodyClip;
			if (!string.IsNullOrEmpty(cur) && !string.IsNullOrEmpty(prefab))
			{
				var (idleClip, _, _) = AnimationCacheService.GetClipName(prefab, idleAction, Forward.x, Forward.y);
				if (BodyHasClip(idleClip) && cur != idleClip)
				{
					Log("Тело: " + key + " с " + cur + " на " + idleClip + " (таймаут)");
					PlayAction(idleAction);
				}
			}
		}

		/// <summary>
		/// Подгоняет CapsuleCollider2D под ВИДИМОЕ тело трупа (EntityModel.TryGetVisualBounds), чтобы клик по
		/// любой точке тела открывал лут и совпадал с кольцом-подсветкой. Боевую капсулу запоминаем один раз и
		/// возвращаем в RestoreLiveCollider при выходе из dead. world→local: коллайдер в системе корня —
		/// offset = InverseTransformPoint(центр bounds), size = мировой размер bounds / |lossyScale| × зазор.
		/// </summary>
		private void FitCorpseCollider()
		{
			if (_corpseCapsule == null) { _corpseCapsule = GetComponent<CapsuleCollider2D>(); if (_corpseCapsule == null) return; }
			if (!TryGetVisualBounds(out Bounds b) || b.size.x < 0.01f || b.size.y < 0.01f) return;

			// Те же границы, что кадром раньше, — капсула уже по ним: запись size/offset пересобирает форму
			// коллайдера, а тело трупа неподвижно почти весь срок.
			if (_capsuleFittedToCorpse && b == _corpseFittedTo) return;

			if (!_liveCapsuleSaved)
			{
				_liveCapsuleSize = _corpseCapsule.size;
				_liveCapsuleOffset = _corpseCapsule.offset;
				_liveCapsuleSaved = true;
			}

			Vector3 lossy = transform.lossyScale;
			float sx = Mathf.Max(Mathf.Abs(lossy.x), 0.0001f);
			float sy = Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
			Vector3 centerLocal = transform.InverseTransformPoint(b.center);
			_corpseCapsule.offset = new Vector2(centerLocal.x, centerLocal.y);
			_corpseCapsule.size = new Vector2(b.size.x / sx * CORPSE_HIT_GAP, b.size.y / sy * CORPSE_HIT_GAP);
			_corpseFittedTo = b;
			_capsuleFittedToCorpse = true;
		}

		/// <summary>Возврат боевой капсулы после выхода трупа из dead (воскрешение/движение). no-op если не подгоняли.</summary>
		private void RestoreLiveCollider()
		{
			if (!_capsuleFittedToCorpse || _corpseCapsule == null) return;
			_corpseCapsule.size = _liveCapsuleSize;
			_corpseCapsule.offset = _liveCapsuleOffset;
			_capsuleFittedToCorpse = false;
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
				// PlayAction сам выберет анимацию по имени action: клип скелета (пакет Spine с сервера),
				// иначе Universal-overlay trigger (dead/remove) — см. EntityModel.PlayAction.
				PlayAction(recive.action);
			}
		}

		/// <summary>
		/// Куда сервер поставит существо шагом из серверной позиции position в направлении direction, либо null —
		/// шага сервер не сделает. Направление — доли шага по осям, как их принимает команда шага (не длиннее
		/// единицы). Зеркало команды шага игры (move/walk/index) вместе с пределом близости к преграде
		/// фреймворка (Map2D::clearance), общего у всех команд шага. Ветки — в порядке сервера: прямо на шаг;
		/// упёрлось — по одной из осей (свободны обе — по большей составляющей направления); у направления вдоль
		/// оси — обход угла диагональным пробником, засчитанный лишь в СОСЕДНЕЙ клетке; прижатие на creep_depth от
		/// центра своей клетки. Найденная точка подтягивается к creep_depth по каждой оси, где соседняя клетка —
		/// преграда, и по паре осей у угловой преграды.
		///
		/// Преграды — этажа, который уровень position.z даёт на карте map: стены другого этажа существо не держат.
		/// И только известные клиенту (MapController.IsColliderCell): клетку без тайла и клетку
		/// недоступной соседней карты сервер тоже держит непроходимой, а клиент о них отсюда не знает. Незнание
		/// читается как «пройти можно»: отсев команд ошибается тогда лишь в сторону лишней отправки, а показ —
		/// в сторону прогноза, который сервер не подтвердит и к позиции которого фигура поправится (Walk).
		///
		/// Координаты — местные координаты карты map: в них стоят и сущность, и преграды карты. Клетка точки —
		/// банковское округление (Mathf.RoundToInt), тем же правилом сервер переводит позицию в клетку; каждое
		/// значение сервер округляет до position_precision, клиент — так же.
		/// </summary>
		public static Vector3? PredictStep(int map, Vector3 position, Vector3 direction)
		{
			Vector3? straight = Straight(map, position, direction);
			if (straight != null)
				return straight;

			float fx = Round(direction.x);
			float fy = Round(direction.y);
			Vector2Int cell = Cell(position);

			Vector3 onlyX = Next(position, fx, 0);
			Vector3 onlyY = Next(position, 0, fy);
			bool canX = fx != 0 && !IsWall(map, position.z, Cell(onlyX));
			bool canY = fy != 0 && !IsWall(map, position.z, Cell(onlyY));

			if (canX && canY)
				return Clearance(map, Mathf.Abs(fx) >= Mathf.Abs(fy) ? onlyX : onlyY);
			if (canX)
				return Clearance(map, onlyX);
			if (canY)
				return Clearance(map, onlyY);

			float corner = ConnectController.corner_offset;
			if (fx == 0 || fy == 0)
			{
				Vector3 first;
				Vector3 second;

				// знак второй оси: сперва та сторона, куда существо уже смещено от центра своей клетки
				if (fy == 0)
				{
					float prefer = position.y >= Mathf.Round(position.y) ? corner : -corner;
					first = Next(position, fx * corner, prefer);
					second = Next(position, fx * corner, -prefer);
				}
				else
				{
					float prefer = position.x >= Mathf.Round(position.x) ? corner : -corner;
					first = Next(position, prefer, fy * corner);
					second = Next(position, -prefer, fy * corner);
				}

				if (Cell(first) != cell && !IsWall(map, position.z, Cell(first)))
					return Clearance(map, first);
				if (Cell(second) != cell && !IsWall(map, position.z, Cell(second)))
					return Clearance(map, second);
			}

			// Прижатие. Сервер его не засчитывает, когда существо в этой точке уже стоит (дистанция чебышевская).
			float depth = ConnectController.creep_depth;
			Vector3 creep = new Vector3(
				fx > 0 ? cell.x + depth : (fx < 0 ? cell.x - depth : position.x),
				fy > 0 ? cell.y + depth : (fy < 0 ? cell.y - depth : position.y),
				position.z);

			if (Mathf.Max(Mathf.Abs(creep.x - position.x), Mathf.Abs(creep.y - position.y)) >= 0.01f && !IsWall(map, position.z, Cell(creep)))
				return Clearance(map, creep);

			return null;
		}

		/// <summary>
		/// Шаг прямо на направление с пределом близости к преграде; null — клетка шага преграда. Общая часть всех
		/// команд шага: дальше каждая идёт своим путём (<see cref="PredictStep"/> — путь команды шага по направлению).
		/// </summary>
		private static Vector3? Straight(int map, Vector3 position, Vector3 direction)
		{
			Vector3 point = Next(position, Round(direction.x), Round(direction.y));
			return IsWall(map, position.z, Cell(point)) ? (Vector3?)null : Clearance(map, point);
		}

		/// <summary>Предел близости к преграде — зеркало Map2D::clearance фреймворка (см. <see cref="PredictStep"/>).</summary>
		private static Vector3 Clearance(int map, Vector3 point)
		{
			float depth = ConnectController.creep_depth;
			float tx = Mathf.Round(point.x);
			float ty = Mathf.Round(point.y);
			int sx = point.x - tx > depth ? 1 : (point.x - tx < -depth ? -1 : 0);
			int sy = point.y - ty > depth ? 1 : (point.y - ty < -depth ? -1 : 0);

			if (sx == 0 && sy == 0)
				return point;

			int cx = (int)tx, cy = (int)ty;
			float x = point.x;
			float y = point.y;

			if (sx != 0 && IsWall(map, point.z, new Vector2Int(cx + sx, cy)))
				x = tx + sx * depth;
			if (sy != 0 && IsWall(map, point.z, new Vector2Int(cx, cy + sy)))
				y = ty + sy * depth;
			if (sx != 0 && sy != 0 && x == point.x && y == point.y && IsWall(map, point.z, new Vector2Int(cx + sx, cy + sy)))
			{
				x = tx + sx * depth;
				y = ty + sy * depth;
			}

			return new Vector3(Round(x), Round(y), point.z);
		}

		/// <summary>Шаг из позиции на направление (fx, fy) — Position::next сервера с его округлением.</summary>
		private static Vector3 Next(Vector3 position, float fx, float fy)
		{
			float step = ConnectController.step;
			return new Vector3(Round(position.x + Round(fx * step)), Round(position.y + Round(fy * step)), position.z);
		}

		private static float Round(float value)
		{
			return (float)Math.Round(value, ConnectController.position_precision);
		}

		private static Vector2Int Cell(Vector3 point)
		{
			return new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
		}

		private static bool IsWall(int map, float z, Vector2Int cell)
		{
			return MapController.IsColliderCell(map, z, cell);
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// она вошла в плагин тк движение нужно в любой игре а координаты часть стандартного функционала, вы можете переопределить ее
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		///
		/// Дойдя до серверной позиции, пока сервер ведёт движение дальше, фигура не ждёт его пакета на месте, а идёт
		/// прогнозом следующего шага — теми же правилами преград, что у сервера (<see cref="PredictStep"/>). Подтверждение —
		/// пакет следующего шага: он запускает новую корутину от того места, куда фигура дошла. Не пришёл за
		/// удвоенный наибольший пинг — сервер шага не сделал, и фигура плавно возвращается на его позицию: остаться
		/// на прогнозе значит стоять там, куда сервер существо не пускал (у преграды — на её краю).
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
			timeout += Time.deltaTime;												   // добавляем кадр тк пакет с новыми координатами может прийти в паузе между кадрами

			// если мы уходим с карты надо замедлиться на время полных пинга
			// мы не првоеряем удаляется ли существо или именно переходит (в обоих случаях action одинаков, но при переходе новая карта указывается) тк при удалении окончательном эта корутина уничтожается с существом
			if (action == ConnectController.ACTION_REMOVE)
            {
				// если расчетное время получения пакета меньше чем обычно анимация шага у персонажа - делаем время анимации шага персонада
				double step = EventTimeout(WalkResponse.GROUP);
				if (timeout < step)
					timeout = step;
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
			//
			// Скорость задаётся в клетках В СЕКУНДУ, а пройденное за виток считается по времени самого витка:
			// шаг перемещения тем самым привязан к частоте отрисовки, а не к частоте расчёта физики. Движение
			// на экране в 120 герц выходит вдвое плавнее, чем на 60, и не зависит от настройки шага физики.
			double stepSpeed = ConnectController.step / timeout;
			double speed = stepSpeed;

			// Фигура идёт прогнозом следующего шага и ждёт его подтверждения сервером.
			bool extrapolation = false;
			DateTime extrapolationStart = DateTime.MinValue;

			// Прогноз сервер не подтвердил — фигура возвращается на его позицию.
			bool correcting = false;

			while (true)
			{
				if (action != "walk" && action != ConnectController.ACTION_REMOVE)
				{
					// Встаём на СЕРВЕРНУЮ позицию, а не на цель корутины: целью бывает и прогноз шага.
					LogWarning("Движение - Сменен action во время движения на " + action+", удаляем корутину");
					transform.localPosition = position;
					break;
				}

				distance = Vector3.Distance(transform.localPosition, finish);

				// путь за этот виток отрисовки
				double distancePerUpdate = speed * Time.deltaTime;

				if (distance < distancePerUpdate)
				{
					if (correcting)
					{
						transform.localPosition = finish;
						break;
					}

					if (extrapolation)
					{
						// Уход с карты подтверждения не ждёт: существо снимается с неё целиком.
						if (action == ConnectController.ACTION_REMOVE)
						{
							Log("Движение - уход с карты дорисован прогнозом");
							break;
						}

						// Подтверждение — пакет следующего шага: он остановит эту корутину и запустит новую.
						if (DateTime.Now < extrapolationStart.AddSeconds(ConnectController.MaxPing() * 2))
						{
							activeLast = DateTime.Now;
							yield return null;
							continue;
						}

						LogWarning("Движение - прогноз шага сервер не подтвердил, возврат с " + transform.localPosition + " на серверную позицию " + position);
						finish = position;
						speed = stepSpeed;
						correcting = true;
						continue;
					}

					// Сервер ведёт движение дальше (либо существо уходит с карты) — идём прогнозом его следующего шага.
					// Направление — Forward как есть: сервер пишет в него фактическое смещение прошлого шага, и его
					// повтор продолжает ту же ветку (скольжение вдоль преграды — на ту же долю клетки), а после
					// прижатия к преграде даёт точку, которую предел близости возвращает на место, — шага нет.
					// Обход преграды по веткам знает только шаг по направлению (index); прочие команды группы
					// (движение к точке, бегство) идут своим маршрутом, и прогнозу у них остаётся шаг прямо.
					Event walking = TryGetEvent(WalkResponse.GROUP);
					bool expected = (walking != null && !string.IsNullOrEmpty(walking.action)) || action == ConnectController.ACTION_REMOVE;
					Vector3? next = null;
					if (expected && Forward != Vector3.zero)
						next = walking != null && walking.action == Response.ACTION_INDEX ? PredictStep(map, finish, Forward) : Straight(map, finish, Forward);

					if (next == null || Vector3.Distance(next.Value, finish) < 0.01f)
					{
						transform.localPosition = finish;
						break;
					}

					extrapolation = true;
					extrapolationStart = DateTime.Now;
					finish = next.Value;
					speed = stepSpeed * 0.7;
					Log("Движение - прогноз следующего шага в " + finish + ", замедление 0.7x");
				}

				activeLast = DateTime.Now;
				transform.localPosition = Vector3.MoveTowards(transform.localPosition, finish, (float)distancePerUpdate);

				Log("Движение - перешли в "+ transform.localPosition +", осталось время "+ GetEventRemain(WalkResponse.GROUP)+" сек., расстояние "+ distance);

				yield return null;
			}
			coroutines.Remove("walk");

			Log("Движение - завершена корутина движения");
			yield break;
		}

		/// <summary>
		/// Кому сервер поручил взять ЭТУ вещь. Поручение приходит командой подбора игрока и живёт у него
		/// (<see cref="PlayerModel.ClaimedPickup"/>): к моменту, когда вещь снимают с карты, сама команда
		/// уже завершена, и спрашивать её в этот миг поздно.
		/// Заявивших бывает и несколько: вещь, подбирающаяся сама (монеты), ставит поручение КАЖДОМУ игроку
		/// в своём радиусе, а возьмёт её один — кто именно, сервер не сообщает. Клик по вещи адресует
		/// поручение одному кликнувшему, но отличить один случай от другого по самому поручению нечем,
		/// поэтому из заявивших всегда берём ближайшего: у единственного заявившего выбор тривиален.
		/// </summary>
		private Transform FindTaker()
		{
			Transform taker = null;
			float best = 0f;

			foreach (PlayerModel candidate in FindObjectsByType<PlayerModel>())
			{
				if (!candidate.ClaimedPickup(key))
					continue;

				float distance = Vector3.Distance(candidate.transform.localPosition, transform.localPosition);

				if (taker == null || distance < best)
				{
					best = distance;
					taker = candidate.transform;
				}
			}

			return taker;
		}

		/// <summary>
		/// анимированное удаление объекта с карты (когда снаряд попал в цель или игрок уходит с карты).
		/// Пытается проиграть ACTION_REMOVE через PlayAction (клип скелета → Universal Animator fallback).
		/// Если ни у скелета, ни в Universal.controller нет данных — удаление мгновенное.
		/// </summary>
		protected override IEnumerator Destroy()
		{
			// Вещь исчезает по серверной правде: она гаснет там, где лежала, — в стороне от персонажа, будто её
			// взяли издалека. Показываем сам подбор: вещь коротко летит к тому, кому сервер поручил её взять,
			// и лишь затем исчезает. Кто это — знает сама команда подбора (её ключи вещей запоминает PlayerModel),
			// поэтому ни дальность подбора, ни выбор игрока клиент не решает.
			if (GetComponent<EquipableGroundMarker>() != null)
			{
				Transform taker = FindTaker();

				Vector3 from = transform.localPosition;
				float flight = taker != null ? Mathf.Min(Vector3.Distance(from, taker.localPosition) / FlightSpeed, FlightMax) : 0f;

				if (flight >= FlightMin)
				{
					for (float passed = 0f; passed < flight && taker != null; passed += Time.deltaTime)
					{
						transform.localPosition = Vector3.Lerp(from, taker.localPosition, passed / flight);
						yield return null;
					}
				}
			}

			// Сущность уходит с карты — сразу снять подсветку-маяк и надпись (Destroy компонента → его
			// OnDestroy сносит нарисованное), не дожидаясь конца remove-анимации (Puff висит пару секунд).
			// Destroy(null) безвреден: обводка есть только у подбираемых предметов (item/экипировка),
			// надпись — у них же и у существ, но не у декора.
			Destroy(GetComponent<EquipableGroundMarker>());
			Destroy(GetComponent<HoverLabel>());

			if (PlayAction(ConnectController.ACTION_REMOVE))
			{
				Log("Удаление - Запуск анимации удаления с карты");

				// Ждём один кадр чтобы Animator либо тело переключились на нужный клип,
				// затем берём его длину.
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
					float length = BodyClipLength;
					if (length > 0.01f)
						yield return new WaitForSeconds(length - 0.01f);
				}
			}

			Log("Удаление - немедленное удаления с карты");
			Destroy(gameObject);

			yield break;
		}
	}
}
