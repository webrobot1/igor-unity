using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	// Content-addressable кеш анимаций игры. Endpoint'ы:
	//   GET /animation/patch/{gameId}/{token}/prefabs?since=  — дельта prefab'ов: {items: slug→entry, all: [slug], version}
	//   GET /animation/patch/{gameId}/{token}/animations     — полный список animation_id → updated_timestamp
	//   GET /animation/patch/{gameId}/{token}/images          — ZIP картинок (sha256.ext) (If-Modified-Since)
	//
	// Сам скелет анимации качает и собирает SpineCacheService своим каналом; отсюда он получает перечень
	// версий (свежесть пакета — та же отметка, что у списка /animations) и общий каталог кеша.
	//
	// Локальный кеш: Application.persistentDataPath/games/{gameId}/animations/
	//   images/{sha256}.{ext}                — распакованные из ZIP /images
	//   structures/{animationId}.spine.json  — кеш пакета скелета (пишет SpineCacheService)
	//   library.json                  — prefab.slug → PrefabEntry (дельта-мёрж по ?since, removal по списку all)
	//   sync.json                     — manifest (archive_last_modified, animation_versions: {id: ts})
	//
	// Справочник компонентов игры (умолчания, состав видов, иконки) приходит СВОИМ каналом и живёт
	// в ComponentCacheService: компонент — элемент игры, не анимации. Здесь он нужен последним звеном
	// цепочки разрешения значения у префаба (GetComponentValue) и именем файла иконки (GetComponentSprite).
	public static class AnimationCacheService
	{
		[DllImport("__Internal")]
		private static extern void JsSync();

		private const string MANIFEST_FILE        = "sync.json";
		private const string LIBRARY_FILE         = "library.json";
		private const string IMAGES_DIR           = "images";
		private const string STRUCT_DIR           = "structures";

		// Версия формата локального кеша (PrefabEntry/library.json). Бамп при смене состава entry
		// (добавление/удаление полей) → EnsureLoaded форсит полный refetch каталога /prefabs (см. cache_schema_version).
		// Бампится и на смену ФОРМЫ значения компонента внутри component_value: состав entry при этом прежний,
		// а разбор идёт по форме — на лежалой записи он падает, и дельта по дате её не подменит (сервер шлёт
		// только записи, изменившиеся с прошлой синхронизации, а у давно не правленного prefab'а дата прежняя).
		private const int CACHE_SCHEMA_VERSION = 8;

		// Разбор серверного payload: сервер шлёт скаляры всегда, включая null (null ≡ дефолт поля), а без
		// Ignore Newtonsoft пишет null в не-nullable поле (version, animation, angle, pivotX/Y, id) и роняет
		// разбор целиком. Одна настройка на оба ответа кеша — /animations и /prefabs.
		private static readonly JsonSerializerSettings SERVER_JSON =
			new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

		private static SyncManifest _manifest;
		private static Dictionary<string, PrefabEntry> _library;                     // prefab.slug → PrefabEntry (дельта-мёрж SyncLibrary)

		private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

		/// <summary>
		/// Tight-rect спрайта в sprite-local мировых единицах; начало координат — PIVOT спрайта (у спрайтов
		/// этого кеша он по центру, см. Sprite.Create ниже), т.е. yMax — высота верхнего края непрозрачных
		/// пикселей НАД центром, и по одной высоте rect'а положение края не восстановить.
		/// Берётся из <see cref="Sprite.vertices"/> — при Tight-меше Unity туда кладёт вершины полигона вокруг
		/// непрозрачных пикселей. <see cref="Sprite.bounds"/> не подходит: он считает всю sprite.rect целиком,
		/// и PNG с прозрачными полями искажают замеры, которыми нормируют размер (тело сущности, надетый
		/// предмет). Требует Tight-меша — у спрайтов этого кеша он задаётся в <see cref="Sprite.Create"/> ниже,
		/// у ассетов Unity — через TextureImporter.spriteMeshType=Tight (см. README / raw .meta files).
		/// </summary>
		public static bool TryGetTightRect(Sprite s, out Rect rect)
		{
			if (s == null) { rect = default; return false; }
			var verts = s.vertices;
			if (verts == null || verts.Length == 0) { rect = default; return false; }
			float minX = verts[0].x, maxX = verts[0].x, minY = verts[0].y, maxY = verts[0].y;
			for (int i = 1; i < verts.Length; i++)
			{
				if (verts[i].x < minX) minX = verts[i].x;
				if (verts[i].x > maxX) maxX = verts[i].x;
				if (verts[i].y < minY) minY = verts[i].y;
				if (verts[i].y > maxY) maxY = verts[i].y;
			}
			rect = new Rect(minX, minY, maxX - minX, maxY - minY);
			return true;
		}

		[Serializable]
		public class SyncManifest
		{
			public string archive_last_modified;
			public Dictionary<int, long> animation_versions = new Dictionary<int, long>();
			// Версия последней дельта-синхронизации /prefabs (unix-сек, max updated отданных entry).
			// Шлётся как ?since в следующий заход — сервер вернёт только изменившиеся с этого момента prefab'ы.
			// 0 (дефолт, в т.ч. для старых sync.json без поля) → холодный старт: полный каталог.
			public long prefab_version;

			// Версия формата локального кеша на диске. При несовпадении с CACHE_SCHEMA_VERSION EnsureLoaded
			// сбрасывает prefab_version→0 (разовый полный refetch каталога уже в новом формате PrefabEntry).
			// 0 (дефолт, в т.ч. старые sync.json без поля) → миграция сработает при первом заходе после апдейта.
			public int cache_schema_version;
		}

		[Serializable]
		// Значения GameImage.rotationMode (wire /prefabs). Должны совпадать с PHP-enum ImageRotationMode.
		public static class RotationMode
		{
			public const string None    = "none";     // каждое направление — свой загруженный вариант
			public const string MirrorX = "mirror_x"; // лево из права зеркалом по X, верх/низ — свои варианты
			public const string Free    = "free";     // один вариант, спрайт крутится по forward (снаряды)
		}

		// Один вариант картинки prefab'а по направлению (images[] из /prefabs).
		[Serializable]
		public class ImageVariant
		{
			public int angle;            // 0=вправо, 90=вверх, 180=влево, 270=вниз (atan2(fwdY,fwdX))
			public string sha256;
			public string extension;
			public float pivotX = 0.5f;  // хват per-вариант (рукоять на разных ракурсах в разных местах)
			public float pivotY = 0.5f;
			[Newtonsoft.Json.JsonIgnore]
			public string File => sha256 + "." + extension;
		}

		// Одна привязка action→clip prefab'а (элемент actions[] из /prefabs, формат общий с серверным
		// PrefabAnimation::actionList). Один action-slug ПОВТОРЯЕТСЯ по клипу на направление; angle —
		// нарисованный facing-угол клипа (0=вправо), null = клип без направления.
		[Serializable]
		public class ActionBinding
		{
			public string action;
			public string clip;
			public int? angle;   // null = клип без направления (прежний ключ "" словарной формы)

			// Повторяется ли клип этого действия. Клиент его не читает: повтор клипа скелета приходит
			// перечнем однократных клипов в самом пакете скелета (SpineCacheService.Loops). Объявлено
			// обязательно — разбор строгий (BaseController), и неизвестное поле роняет манифест целиком.
			public bool? looping;

			// Серверный номер записи привязки. Клиенту не нужен, но объявлен обязательно: разбор
			// строгий (BaseController), и неизвестное поле роняет манифест целиком — а с ним вход в игру.
			public int id;
		}

		public class PrefabEntry
		{
			public string prefab;
			public int animation;

			/// <summary>
			/// Имя нужного варианта скелета внутри анимации: одна анимация несёт НЕСКОЛЬКО вариантов (вариации
			/// листа перекраской — разные prefab на разные варианты одной анимации). Приходит в /prefabs
			/// (PatchController::prefabs). По нему вариант и выбирается в пакете скелета (SpineCacheService).
			/// Null/пусто — варианта не назвали: скелет собрать нечем.
			/// </summary>
			public string entity;

			/// <summary>
			/// Привязки action→clip prefab'а плоским списком (приходит per-entry в /prefabs). Один action-slug
			/// ПОВТОРЯЕТСЯ по клипу на направление; ActionBinding.angle — нарисованный facing-угол (0=вправо),
			/// null = клип без направления. Резолвится AnimationCacheService.GetClipName/Simple (фильтр списка
			/// по action + выбор клипа по углу). Null/отсутствует — у prefab'а нет action-привязок (image-prefab
			/// либо не настроено в админке). Пустой набор сервер шлёт как [] (список, каста в объект не требует).
			/// </summary>
			public List<ActionBinding> actions;

			/// <summary>
			/// ДЕЛИТЕЛЬ целевой высоты тела (per-prefab константа, приходит с /prefabs, задаётся в админке при
			/// конфигурации prefab'а): высота тела в клетках объявлена на сервере, на клиент уходит обратная
			/// ей величина. Клиент приводит тело к высоте 1/size клеток — SpineVisualBuilder.Fit у скелета,
			/// UpdateController.ApplyVisualPrefab у картинки. Габариты самой фигуры замеряются у неё всегда.
			/// Если null (поле не задано на сервере) — тело приводится к одной клетке.
			/// </summary>
			public float? size;
			public bool h_mirror;

			/// <summary>
			/// SHA256 изображения для статичных image-prefab'ов (без анимации). Null/пусто — у prefab'а есть скелет.
			/// Полное имя файла в clientArchive = sha256 + "." + extension.
			/// </summary>
			public string sha256;

			/// <summary>
			/// Расширение файла (png/jpg/jpeg/gif). Приходит только если sha256 не пуст.
			/// </summary>
			public string extension;

			/// <summary>
			/// Читаемое имя prefab'а для UI (Prefab.name из админки) — единый источник и для статичных,
			/// и для анимированных prefab'ов: сервер кладёт сюда только Prefab.name, в wire привязки
			/// картинки (image-prefab) имя НЕ шлётся (skill animation «Имя предмета — у потребителя»).
			/// Ключа нет (имя не задано) → поле null, клиент показывает сам prefab-slug. Не используется
			/// для построения путей.
			/// </summary>
			public string name;

			/// <summary>
			/// Описание prefab'а (Prefab.description из админки) для UI-тултипов/деталей предмета.
			/// Null — описание не задано. Приходит в /prefabs вместе с name (дельтой по ?since).
			/// </summary>
			public string description;

			/// <summary>
			/// Slug вида сущности (kind), к которому относится этот prefab. Выводится на сервере из самого
			/// prefab'а (slug→kind однозначен) и приходит в каждом entry /prefabs (и для скелетных, и для
			/// картиночных prefab'ов). Клиент использует его как имя Resources-префаба (Prefabs/{kind}):
			/// в пакетах сущностей kind не едет.
			/// </summary>
			public string kind;

			/// <summary>
			/// Режим достройки недостающих направлений (GameImage.rotationMode): RotationMode.None —
			/// каждое направление свой вариант; MirrorX — лево из права зеркалом по X (default);
			/// Free — один вариант, спрайт крутится по forward (снаряды/фаерболы; бывший rotatable=true).
			/// Общий для всех вариантов prefab'а, приходит в плоских полях /prefabs.
			/// </summary>
			public string rotationMode = RotationMode.MirrorX;

			/// <summary>
			/// Направление, под которое нарисован канонический спрайт (плоские sha256/extension/pivot):
			/// 0=вправо (default). Канон = вариант с angle ближайшим к 0 — инвентарь/земля рисуют его
			/// БЕЗ поворота; Free-поворот по forward считается как forward − angle.
			/// </summary>
			public int angle;

			/// <summary>
			/// Все варианты картинки по направлениям (images[] из /prefabs, angle ASC, включая канон).
			/// Null/пусто — не image-prefab; для экипировки см. GetPrefabImageVariants (синтез из плоских
			/// полей канона при отсутствии). На персонаже WeaponMount берёт ближайший к forward.
			/// </summary>
			public System.Collections.Generic.List<ImageVariant> images;

			/// <summary>
			/// Pivot хвата (0..1, Unity-конвенция: 0=низ/лево, 1=верх/право) для надетого оружия —
			/// точка крепления к якорю скелета и центр вращения. Приходит для image-prefab'ов из
			/// /prefabs (GameImage.pivotX/Y). Default 0.5/0.5 (центр) — для не-оружейных картинок.
			/// </summary>
			public float pivotX = 0.5f;
			public float pivotY = 0.5f;

			/// <summary>
			/// Slot-slug-и из Game.equipmentSlot куда этот prefab может быть надет (item-prefab → [hand_r, hand_l] и т.п.).
			/// Контракт по экипируемости (сервер кодирует именно так, см. Animation/PatchController):
			///   null                       — kind НЕ экипируемый: предмет нельзя надеть в принципе;
			///   список (в т.ч. пустой [])  — kind экипируемый; пустой [] = «слоты ещё не заданы», но предмет
			///                                экипируемый. Т.е. экипируемость = (equipable_slot != null), НЕ (Count > 0):
			///                                null и пустой список — РАЗНЫЕ вещи, не путать (в C# различимы).
			/// Применение: при экипировке клиент пересекает этот список с object_slot носителя (из пакета скелета)
			/// → находит anchor для отрисовки. Item всегда с какой-то графикой (анимация или статичная картинка) —
			/// prefab без графики экипируемым быть не должен (иначе клиент рисует unknown-спрайт).
			/// </summary>
			public System.Collections.Generic.List<string> equipable_slot;

			/// <summary>
			/// Эффективные значения компонентов вида этого prefab'а: slug → значение (заданное prefab'у, иначе
			/// умолчание справочника компонента) — то же, что получит сущность этого prefab'а при создании.
			/// Ключа нет вовсе — у вида нет компонентов с действующим значением. Форма значения задана типом
			/// компонента: число/строка у скалярных, объект либо массив у структурных, — потому JToken,
			/// а не строка.
			/// Ключи набора несут и СОСТАВ: отдельного списка имён сервер не шлёт, компонент без действующего
			/// значения в набор не идёт (GetPrefabComponents отдаёт эти же ключи).
			/// Общее значение свойства предмета живёт ЗДЕСЬ, а не в слоте хранилища: слот несёт только
			/// отличия экземпляра (см. GetComponentValue).
			/// Набор справочный — он про КОНТЕНТ, одинаковый у всех: свойства ЖИВОЙ сущности (чьё здоровье
			/// кому видно) приходят своим каналом, и показывать их надо оттуда, иначе наружу полезет то,
			/// что игроку знать не положено (шансы дропа лежат тут же).
			/// </summary>
			public Dictionary<string, JToken> component_value;

			public bool IsImage => !string.IsNullOrEmpty(sha256);

			/// <summary>Полное имя файла спрайта (sha256.extension) или null если у prefab'а скелет.</summary>
			[Newtonsoft.Json.JsonIgnore]
			public string ImageFile => IsImage ? sha256 + "." + extension : null;
		}

		// Возвращает per-prefab "size" (высота тела в единицах скелета) из library, если задан, иначе null.
		// Используется сборкой визуала для точной нормализации размера без замера габаритов.
		// Контракт: вызывать ТОЛЬКО после того как SyncAll отработал (что гарантировано
		// SigninController.LoadMain — он awaitит SyncAll до ConnectController.Connect, поэтому
		// любой WS-спавн приходит уже с загруженным _library). Вызов до SyncAll — это баг вызывающей стороны,
		// поэтому бросаем exception вместо тихого null: null-возврат от «prefab без size» и null от «library
		// не загружена» — разные вещи, и глотать второе опасно (сборка визуала уйдёт в замер габаритов,
		// замаскировав проблему timing'а).
		public static float? GetPrefabSize(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabSize вызван до SyncAll (_library == null). prefab=" + prefab + ". Вызывайте только после завершения SigninController.LoadMain.");
			return _library.TryGetValue(prefab, out PrefabEntry e) ? e.size : (float?)null;
		}

		// Анимация, чей скелет носит prefab. 0 — скелета у него нет вовсе (набор картинок либо только вид):
		// легитимное отсутствие, потому не исключение. Контракт по _library как у GetPrefabSize.
		public static int GetPrefabAnimation(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabAnimation вызван до SyncAll (_library == null). prefab=" + prefab);
			return _library.TryGetValue(prefab, out PrefabEntry e) ? e.animation : 0;
		}

		// Имя нужного варианта скелета из /prefabs-привязки: несколько вариантов в одной анимации (вариации
		// листа перекраской) → клиент выбирает вариант по имени. Контракт по _library как у GetPrefabSize.
		// Null/пусто — варианта не назвали: скелет собрать нечем (SpineCacheService.GetSkeleton).
		public static string GetPrefabEntity(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabEntity вызван до SyncAll (_library == null). prefab=" + prefab);
			return _library.TryGetValue(prefab, out PrefabEntry e) ? e.entity : null;
		}

		// Режим достройки направлений картинки (RotationMode.*). Контракт по _library тот же что у
		// GetPrefabSize — вызывать только после SyncAll, иначе exception (вызов с _library==null — баг:
		// ре-резолв forward до загрузки библиотеки; тихий default замаскировал бы timing-баг).
		// Default MirrorX — для «prefab не в библиотеке / поле не пришло» (скелетные prefab'ы, player/enemy).
		// Free используется в EntityModel при ре-резолве forward статичных image-prefab'ов (бывш. rotatable).
		public static string GetPrefabRotationMode(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabRotationMode вызван до SyncAll (_library == null). prefab=" + prefab);
			if (string.IsNullOrEmpty(prefab))
				return RotationMode.MirrorX;
			return _library.TryGetValue(prefab, out PrefabEntry e) && !string.IsNullOrEmpty(e.rotationMode)
				? e.rotationMode : RotationMode.MirrorX;
		}

		// Опорное направление канонического спрайта (PrefabEntry.angle, 0=вправо). Free-поворот по
		// forward считается как forward − этот угол: спрайт, нарисованный «вверх» (angle=90), тоже
		// полетит остриём по курсу. Контракт по _library как у GetPrefabRotationMode.
		public static int GetPrefabAngle(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabAngle вызван до SyncAll (_library == null). prefab=" + prefab);
			return !string.IsNullOrEmpty(prefab) && _library.TryGetValue(prefab, out PrefabEntry e) ? e.angle : 0;
		}

		// Варианты картинки prefab'а по направлениям — для экипировки (WeaponMount подменяет спрайт по
		// forward носителя). Для image-prefab'а всегда ≥1 элемента: при пустом images[] синтезируется
		// один вариант из плоских полей канона. Null — prefab не image (скелет) или не в библиотеке.
		// Контракт по _library как у GetPrefabRotationMode.
		public static System.Collections.Generic.List<ImageVariant> GetPrefabImageVariants(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabImageVariants вызван до SyncAll (_library == null). prefab=" + prefab);
			if (string.IsNullOrEmpty(prefab) || !_library.TryGetValue(prefab, out PrefabEntry e) || !e.IsImage)
				return null;
			if (e.images != null && e.images.Count > 0)
				return e.images;
			return new System.Collections.Generic.List<ImageVariant> {
				new ImageVariant { angle = e.angle, sha256 = e.sha256, extension = e.extension, pivotX = e.pivotX, pivotY = e.pivotY },
			};
		}

		// Список slot-slug-ов в которые prefab может быть экипирован (item-prefab → [hand_r, hand_l] и т.п.).
		// Это «куда можно надеть», НЕ «экипируем ли вообще»: и null (не экипируемый), и пустой список
		// (экипируемый, но слоты не заданы) → возвращаем пустой список, тк droppable-слотов нет в обоих случаях.
		// Саму экипируемость отличают по equipable_slot != null (см. doc поля), не по этому методу.
		// Контракт по _library тот же что у GetPrefabSize — вызывать только после SyncAll, иначе exception
		// (для UX-greying-out тихий fallback опасен: пометит экипируемый item как «нельзя надеть» из-за гонки загрузки).
		public static System.Collections.Generic.List<string> GetEquipableSlots(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetEquipableSlots вызван до SyncAll (_library == null). prefab=" + prefab);
			if (string.IsNullOrEmpty(prefab) || !_library.TryGetValue(prefab, out PrefabEntry e) || e.equipable_slot == null)
				return new System.Collections.Generic.List<string>();
			return e.equipable_slot;
		}

		// Компоненты, ДЕЙСТВУЮЩИЕ у prefab'а: заданные ему плюс положенные его виду (у последних значение даёт
		// умолчание справочника). По этому составу игровые механики клиента отличают виды сущностей — что это
		// за существо или объект. Контракт по _library тот же, что у GetPrefabSize — throw на _library==null
		// (timing-баг: вызов до SyncAll). «Prefab нет в library» → пустой список: легитимное отсутствие,
		// вызывающий просто не находит искомого.
		public static System.Collections.Generic.List<string> GetPrefabComponents(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabComponents вызван до SyncAll (_library == null). prefab=" + prefab);

			var names = new System.Collections.Generic.List<string>();
			if (string.IsNullOrEmpty(prefab) || !_library.TryGetValue(prefab, out PrefabEntry e))
				return names;

			if (e.component_value != null)
				names.AddRange(e.component_value.Keys);

			if (!string.IsNullOrEmpty(e.kind))
				foreach (string slug in ComponentCacheService.GetSlugs())
				{
					var kinds = ComponentCacheService.GetKinds(slug);
					if (kinds != null && kinds.Contains(e.kind) && !names.Contains(slug))
						names.Add(slug);
				}

			return names;
		}

		// Значение компонента у КОНКРЕТНОГО экземпляра — цепочкой «своё → заданное prefab'у → умолчание
		// компонента». Единственная точка этого правила: потребители (цена предмета и т.п.) спрашивают
		// значение только здесь, и та же цепочка действует на сервере при создании сущности.
		// own — компоненты слота хранилища (инвентарь, добыча): сервер кладёт туда ТОЛЬКО отличия
		// экземпляра от prefab'а, общее значение туда не копируется — иначе правка prefab'а до предмета
		// не доезжала бы, а разный набор компонентов разводил бы одинаковые предметы по разным позициям.
		// Контракт по _library тот же, что у GetPrefabComponents — throw на _library==null (вызов до SyncAll).
		// null — значения нет ни у экземпляра, ни у prefab'а, ни в умолчании (компонент виду не положен либо
		// значения не несёт): легитимное отсутствие, вызывающий решает сам (нет цены — предмет вне торговли).
		public static JToken GetComponentValue(string prefab, string component, IDictionary<string, string> own)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetComponentValue вызван до SyncAll (_library == null). prefab=" + prefab + ", component=" + component);
			if (string.IsNullOrEmpty(component))
				return null;

			if (own != null && own.TryGetValue(component, out string mine) && mine != null)
				return mine;

			if (!string.IsNullOrEmpty(prefab) && _library.TryGetValue(prefab, out PrefabEntry e))
			{
				if (e.component_value != null && e.component_value.TryGetValue(component, out JToken value) && value != null && value.Type != JTokenType.Null)
					return value;

				// Умолчание действует, только если компонент положен ВИДУ этого prefab'а: у чужого вида
				// значения быть не должно, иначе предмет получил бы свойство существа.
				var kinds = ComponentCacheService.GetKinds(component);
				if (kinds != null && !string.IsNullOrEmpty(e.kind) && kinds.Contains(e.kind))
					return ComponentCacheService.GetDefault(component);
			}

			return null;
		}

		// ВЕСЬ набор свойств конкретного экземпляра разом: slug → эффективное значение по тому же правилу
		// («своё, иначе префабное»), что и точечный GetComponentValue, — оно и вызывается на каждый ключ,
		// второй копии правила тут нет. Ключи — объединение префабных и своих: экземпляр может нести и то,
		// чего у вида нет вовсе.
		// Собирается ОДИН раз при разборе слота хранилища (см. InventoryController.RenderSlotItem), дальше
		// предмет отвечает о себе сам, а окна читают готовые значения и правила разрешения не повторяют:
		// иначе каждый новый компонент предмета (атака, защита) пришлось бы подключать в каждом окне.
		// Снимок: манифест префабов клиент тянет один раз за вход, и значения вида за сессию не меняются.
		// Контракт по _library тот же, что у GetComponentValue — throw на _library==null (вызов до SyncAll).
		public static Dictionary<string, JToken> GetComponentValues(string prefab, IDictionary<string, string> own)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetComponentValues вызван до SyncAll (_library == null). prefab=" + prefab);

			Dictionary<string, JToken> values = new Dictionary<string, JToken>();

			foreach (string slug in GetPrefabComponents(prefab))
				values[slug] = GetComponentValue(prefab, slug, own);

			if (own != null)
				foreach (string slug in own.Keys)
					if (!values.ContainsKey(slug))
						values[slug] = GetComponentValue(prefab, slug, own);

			return values;
		}

		// Готовый Sprite иконки компонента — пара к GetPrefabSprite: то же чтение картинки из локального
		// кеша, только имя файла берётся из справочника компонентов, а не из entry префаба.
		// null — иконки у компонента нет либо картинка битая (битый кеш чистится TryGetSprite,
		// перекачается на следующем sync); показу этого довольно, чтобы остаться текстовым.
		// Контракт по справочнику — через ComponentCacheService.GetImage (throw на вызове до его загрузки).
		public static Sprite GetComponentSprite(int gameId, string component)
		{
			string imageFile = ComponentCacheService.GetImage(component);
			if (imageFile == null) return null;
			try { return TryGetSprite(gameId, imageFile); }
			catch (Exception ex) { Debug.LogWarning("GetComponentSprite '" + component + "': " + ex.Message); return null; }
		}

		// Подбираемый ли это «предмет на земле» (для подсветки/надписи лежащих вещей в мире).
		// Критерий (геймдизайн): kind == "item" (предмет-вещь, в т.ч. расходник) ЛИБО экипируемый.
		// Экипируемость = (equipable_slot != null), а НЕ (Count > 0): пустой [] — тоже экипируемый
		// предмет (слоты ещё не заданы), его игрок может подобрать и кликнуть — см. doc поля equipable_slot.
		// Контракт по _library: throw на _library==null (timing-баг — вызов до SyncAll, как у прочих геттеров).
		// Но «prefab нет в library / kind не item / не экипируем» → false: это легитимное «не предмет»,
		// у вызывающего корректная реакция (не подсвечивать). В отличие от GetPrefabKind, где kind обязан
		// резолвиться для рендера, здесь отсутствие — норма, поэтому дефолт false, а не throw.
		public static bool IsGroundItem(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.IsGroundItem вызван до SyncAll (_library == null). prefab=" + prefab);
			if (string.IsNullOrEmpty(prefab) || !_library.TryGetValue(prefab, out PrefabEntry e))
				return false;
			return e.kind == "item" || e.equipable_slot != null;
		}

		// Человекочитаемое имя prefab'а (PrefabEntry.name — Prefab.name из админки) для UI-надписей над
		// предметами на земле. Имя опционально: сервер кладёт name только если задан, иначе ключа нет → e.name
		// == null. Пустое/отсутствующее → возвращаем null, чтобы вызывающий сделал фолбэк на сам prefab-slug
		// (он у него уже есть: `GetPrefabName(p) ?? p`). Контракт по _library тот же (throw на _library==null).
		public static string GetPrefabName(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabName вызван до SyncAll (_library == null). prefab=" + prefab);
			return !string.IsNullOrEmpty(prefab) && _library.TryGetValue(prefab, out PrefabEntry e) && !string.IsNullOrEmpty(e.name) ? e.name : null;
		}

		// Описание prefab'а (PrefabEntry.description — Prefab.description из админки) для UI-тултипов/деталей
		// предмета. Контракт по _library тот же (throw на _library==null — вызов до SyncAll). null — описание
		// не задано (легитимно: вызывающий просто не показывает блок описания).
		public static string GetPrefabDescription(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabDescription вызван до SyncAll (_library == null). prefab=" + prefab);
			return !string.IsNullOrEmpty(prefab) && _library.TryGetValue(prefab, out PrefabEntry e) ? e.description : null;
		}

		// Действия prefab'а (slug'и action его привязок, без повторов и в порядке сервера). Нужны показу,
		// который перебирает, что существо умеет делать: сами клипы там резолвятся обычным GetClipName —
		// с нужным ракурсом, поэтому здесь только действия, не клипы. Порядок сохраняем: он задан админкой
		// и в нём action'ы читаются осмысленно (покой, ходьба, удар), а не как попало.
		// Контракт по _library тот же (throw на _library==null — вызов до SyncAll); пустой список —
		// у prefab'а нет привязок (image-prefab либо не настроено).
		public static System.Collections.Generic.List<string> GetPrefabActions(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabActions вызван до SyncAll (_library == null). prefab=" + prefab);

			var result = new System.Collections.Generic.List<string>();
			if (string.IsNullOrEmpty(prefab) || !_library.TryGetValue(prefab, out PrefabEntry e) || e.actions == null)
				return result;

			foreach (var binding in e.actions)
				if (binding != null && !string.IsNullOrEmpty(binding.action) && !result.Contains(binding.action))
					result.Add(binding.action);

			return result;
		}

		// Извлекает текст серверной ошибки из body ({"error":"..."} — exceptionHandler и явные 4xx)
		// при неуспешном HTTP-запросе. Fallback — код+generic error от UnityWebRequest.
		private static string ExtractError(UnityWebRequest req)
		{
			string body = req.downloadHandler?.text;
			if (!string.IsNullOrEmpty(body))
			{
				try
				{
					var err = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
					if (err != null && err.TryGetValue("error", out string msg) && !string.IsNullOrEmpty(msg))
						return msg;
				}
				catch { }
			}
			return req.responseCode + " " + req.error;
		}

		// Корень кеша анимаций для игры
		private static string AnimationsPath(int gameId)
		{
			string folder;
			#if UNITY_WEBGL && !UNITY_EDITOR
				folder = "idbfs";
			#else
				folder = Application.persistentDataPath;
			#endif
			string path = Path.Combine(folder, "games", gameId.ToString(), "animations");
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			return path;
		}

		private static string ImagesPath(int gameId)          => Path.Combine(AnimationsPath(gameId), IMAGES_DIR);
		private static string StructPath(int gameId)          => Path.Combine(AnimationsPath(gameId), STRUCT_DIR);

		// Те же каталоги кешу скелетов Spine: картинки у него общие с этим кешем (страницы атласа — это они
		// же), а пакет скелета лежит в каталоге структур своей анимации. Своих каталогов он не заводит —
		// иначе сброс кеша анимаций оставлял бы их сиротами.
		public static string ImagesDirPath(int gameId)        => ImagesPath(gameId);
		public static string StructuresPath(int gameId)       => StructPath(gameId);
		private static string ManifestPath(int gameId)        => Path.Combine(AnimationsPath(gameId), MANIFEST_FILE);
		private static string LibraryPath(int gameId)         => Path.Combine(AnimationsPath(gameId), LIBRARY_FILE);

		// Загружает manifest + library + files с диска. Идемпотентно.
		private static void EnsureLoaded(int gameId)
		{
			string mp = ManifestPath(gameId);
			// Рассинхрон disk↔RAM (sync.json удалён внешним кодом / ручной очисткой кэша, но _manifest
			// в RAM держит timestamp прошлого архива) — нарушение контракта: AnimationCacheService —
			// единственный владелец этих файлов. Падаем громко (skill code «Отказ и дефолт»), чтобы виновный
			// код был починен у источника, а не маскировался силент-ресетом.
			if (_manifest != null && !File.Exists(mp))
				throw new InvalidOperationException("AnimationCache: sync.json отсутствует на диске, но _manifest загружен в RAM. Кто-то очистил кэш мимо ResetCache() — почините источник.");
			if (_manifest == null)
			{
				_manifest = File.Exists(mp)
					? JsonConvert.DeserializeObject<SyncManifest>(File.ReadAllText(mp))
					: new SyncManifest();
				// Миграция схемы кеша: состав PrefabEntry расширился (напр. поле actions), а since-дельта
				// /prefabs для НЕизменившихся prefab'ов вернула бы пустоту — старый library.json остался бы
				// без новых полей (actions=null → анимации не резолвятся). При смене версии формата разово
				// форсим полный refetch каталога: prefab_version→0 (since=0 в SyncLibrary тянет весь каталог).
				if (_manifest.cache_schema_version != CACHE_SCHEMA_VERSION)
				{
					_manifest.cache_schema_version = CACHE_SCHEMA_VERSION;
					_manifest.prefab_version = 0;
					// Отметки версий анимаций сбрасываем вместе с каталогом: они говорят, ЧТО лежит в кеше
					// анимации, а смена формата этого кеша делает лежащее негодным — без сброса отметка
					// считала бы годным файл прежней формы.
					_manifest.animation_versions.Clear();
					SaveManifest(gameId);
				}
			}
			if (_library == null)
			{
				string lp = LibraryPath(gameId);
				_library = new Dictionary<string, PrefabEntry>();
				if (File.Exists(lp))
				{
					// Старый формат (string→int) не парсится на новый — catch, начнём с пустого; SyncLibrary всё равно перезальёт целиком.
					try { _library = JsonConvert.DeserializeObject<Dictionary<string, PrefabEntry>>(File.ReadAllText(lp)) ?? new Dictionary<string, PrefabEntry>(); }
					catch { _library = new Dictionary<string, PrefabEntry>(); }
				}
			}
			if (!Directory.Exists(ImagesPath(gameId))) Directory.CreateDirectory(ImagesPath(gameId));
			if (!Directory.Exists(StructPath(gameId))) Directory.CreateDirectory(StructPath(gameId));
		}

		private static void SaveManifest(int gameId)
		{
			File.WriteAllText(ManifestPath(gameId), JsonConvert.SerializeObject(_manifest));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		private static void SaveLibrary(int gameId)
		{
			File.WriteAllText(LibraryPath(gameId), JsonConvert.SerializeObject(_library));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полный сброс локального кеша анимаций игры: manifest, library, structures/, images/.
		// Вызывается при обнаружении рассинхронизации (например, сервер отвечает 404 на animation_id из library).
		// После сброса следующий SyncAll пересобирает всё с нуля.
		public static void ResetCache(int gameId)
		{
			Debug.LogWarning("AnimationCache: сброс кеша игры " + gameId);
			// null, а не пустые объекты: EnsureLoaded проверяет «_manifest != null && !File.Exists(mp)»
			// и бросает исключение. Если оставить здесь new SyncManifest() — следующий SyncAll в той же
			// сессии (повторный логин после Error) упадёт на этом guard'е.
			_manifest = null;
			_library = null;
			_spriteCache.Clear();
			// Каталог структур сносится ниже целиком — разобранное из него в памяти пережило бы снос и
			// осталось бы отвечать по снятым файлам (память живёт до остановки игры, не до сброса кеша).
			SpineCacheService.Reset();

			try
			{
				if (File.Exists(ManifestPath(gameId)))       File.Delete(ManifestPath(gameId));
				if (File.Exists(LibraryPath(gameId)))        File.Delete(LibraryPath(gameId));
				if (Directory.Exists(StructPath(gameId))) Directory.Delete(StructPath(gameId), true);
				if (Directory.Exists(ImagesPath(gameId))) Directory.Delete(ImagesPath(gameId), true);
			}
			catch (Exception ex) { Debug.LogWarning("AnimationCache: ошибка при сбросе кеша: " + ex.Message); }

			Directory.CreateDirectory(StructPath(gameId));
			Directory.CreateDirectory(ImagesPath(gameId));
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Полная синхронизация перед входом в игру: архив картинок + library + версии анимаций + предзагрузка скелетов. Вызывать ДО Connect.
		// Привязки action→clip приходят per-prefab в /prefabs (PrefabEntry.actions списком), качаются здесь через SyncLibrary.
		// onProgress — доля пройденных шагов (0..1) для полосы загрузки. Доля ВНУТРИ шага здесь не считается:
		// шагов четыре, и полоса движется их сменой; долю принятых байт отдаёт кеш тайлов, качающий один
		// большой архив, где без неё полоса стояла бы всё скачивание.
		public static IEnumerator SyncAll(string host, int gameId, string token, Action<string> onError = null, Action<float> onProgress = null)
		{
			EnsureLoaded(gameId);
			yield return SyncImagesArchive(host, gameId, token, onError);
			onProgress?.Invoke(0.25f);
			yield return SyncLibrary(host, gameId, token, onError);
			onProgress?.Invoke(0.5f);
			var versions = new Dictionary<int, long>();
			yield return SyncAnimations(host, gameId, token, versions, onError);
			onProgress?.Invoke(0.75f);
			yield return PreFetchSkeletons(host, gameId, token, versions, onError);
			onProgress?.Invoke(1f);
		}

		// Полный список animation_id → updated: кладём его вызывающему (по нему предзагрузка решает, что
		// перекачать) и снимаем кеш анимаций, которых на сервере больше нет.
		private static IEnumerator SyncAnimations(string host, int gameId, string token, Dictionary<int, long> versions, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/animations";
			Debug.Log("Запрашиваю список анимаций " + url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache animations: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			Dictionary<int, long> serverVersions;
			try { serverVersions = JsonConvert.DeserializeObject<Dictionary<int, long>>(text, SERVER_JSON); }
			catch (Exception ex) { onError?.Invoke("AnimationCache animations parse: " + ex.Message); yield break; }

			if (serverVersions == null) serverVersions = new Dictionary<int, long>();

			foreach (var kv in serverVersions)
				versions[kv.Key] = kv.Value;

			// Удалить локальные анимации которых больше нет на сервере
			var toRemove = new List<int>();
			foreach (var id in _manifest.animation_versions.Keys)
				if (!serverVersions.ContainsKey(id))
					toRemove.Add(id);
			foreach (var id in toRemove)
			{
				_manifest.animation_versions.Remove(id);
				SpineCacheService.Drop(gameId, id);
			}

			Debug.Log("AnimationCache: анимаций у сервера " + versions.Count + ", удалено " + toRemove.Count);
			SaveManifest(gameId);
		}

		// Предзагрузка скелетов: к спавну существа пакет его анимации обязан лежать на диске, иначе десяток
		// существ одного вида заведёт десяток запросов за одним файлом. Версия пакета сверяется с серверной:
		// разошлась — прежний снимается и качается заново. Отметку ставим ПОСЛЕ удачной закачки — сорвалась,
		// и файла на диске нет: следующий заход попробует снова.
		private static IEnumerator PreFetchSkeletons(string host, int gameId, string token, Dictionary<int, long> versions, Action<string> onError)
		{
			var seen = new HashSet<int>();
			foreach (var kv in _library)
			{
				int animationId = kv.Value.animation;
				// animation == 0 → скелета нет вовсе (image-prefab или kind-only prefab): качать нечего.
				if (animationId == 0) continue;
				if (!seen.Add(animationId)) continue;

				versions.TryGetValue(animationId, out long remote);
				if (!_manifest.animation_versions.TryGetValue(animationId, out long local) || local != remote)
					SpineCacheService.Drop(gameId, animationId);

				string failure = null;
				yield return SpineCacheService.Ensure(host, gameId, animationId, token, error =>
				{
					failure = error;
					onError?.Invoke(error);
				});
				if (failure != null) continue;

				_manifest.animation_versions[animationId] = remote;
			}
			SaveManifest(gameId);
		}

		// Архив: GET с If-Modified-Since. 304 → ничего. 200 → unzip в images/.
		private static IEnumerator SyncImagesArchive(string host, int gameId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/images";
			Debug.Log("Запрашиваю архив картинок анимаций "+url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			if (!string.IsNullOrEmpty(_manifest.archive_last_modified))
				req.SetRequestHeader("If-Modified-Since", _manifest.archive_last_modified);
			req.downloadHandler = new DownloadHandlerBuffer();

			yield return req.SendWebRequest();

			if (req.responseCode == 304)
			{
				Debug.Log("AnimationCache: архив картинок актуален (кеш)");
				req.Dispose();
				yield break;
			}
			// 202 = building, попробуем в следующий заход
			if (req.responseCode == 202)
			{
				Debug.Log("AnimationCache: архив пересобирается на сервере, повторим позже");
				req.Dispose();
				yield break;
			}
			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache archive: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string lastMod = req.GetResponseHeader("Last-Modified");
			int extractedCount = 0;

			if(req.downloadedBytes>0)
			{
				byte[] zipBytes = req.downloadHandler.data;
				try
				{
					string imagesDir = ImagesPath(gameId);
					using (var ms = new MemoryStream(zipBytes))
					using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
					{
						foreach (var entry in zip.Entries)
						{
							if (string.IsNullOrEmpty(entry.Name)) continue;
							string dest = Path.Combine(imagesDir, entry.Name);
							using (var src = entry.Open())
							using (var dst = File.Create(dest))
							{
								src.CopyTo(dst);
							}
							extractedCount++;
						}
					}
				}
				catch (Exception ex)
				{
					onError?.Invoke("AnimationCache archive unzip: " + ex.Message);
					yield break;
				}
			}
			req.Dispose();
			
			Debug.Log("AnimationCache: архив картинок обновлён, распаковано " + extractedCount + " файлов");
			_manifest.archive_last_modified = lastMod;
			SaveManifest(gameId);
			_spriteCache.Clear(); // новые картинки могли появиться
			#if UNITY_WEBGL && !UNITY_EDITOR
				JsSync();
			#endif
		}

		// Конверт дельта-ответа /prefabs?since= (см. серверный Animation/PatchController::prefabs).
		[Serializable]
		private class PrefabSyncResponse
		{
			public Dictionary<string, PrefabEntry> items;  // только изменившиеся с since (slug → entry)
			public List<string> all;                       // все текущие slug игры (для детекции удалений)
			public long version;                            // max updated отданных items — клиент шлёт как since далее
		}

		// Дельта-синхронизация библиотеки prefab'ов. Мёржит изменившиеся entry в _library и удаляет slug'и,
		// которых больше нет в all. since = prefab_version из манифеста, НО только если _library не пуста:
		// при потере кэша (library.json удалён мимо ResetCache, манифест уцелел) since=0 форсит полный ресинк,
		// иначе дельта прислала бы только изменившиеся, а неизменные prefab'ы остались бы потеряны.
		private static IEnumerator SyncLibrary(string host, int gameId, string token, Action<string> onError)
		{
			long since = (_library != null && _library.Count > 0) ? _manifest.prefab_version : 0;
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token + "/prefabs?since=" + since;
			Debug.Log("Запрашиваю список префабов " + url);

			UnityWebRequest req = UnityWebRequest.Get(url);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("AnimationCache library: " + ExtractError(req));
				req.Dispose();
				yield break;
			}

			string text = req.downloadHandler.text;
			req.Dispose();

			PrefabSyncResponse parsed;
			try { parsed = JsonConvert.DeserializeObject<PrefabSyncResponse>(text, SERVER_JSON); }
			catch (Exception ex) { onError?.Invoke("AnimationCache library parse: " + ex.Message); yield break; }

			if (parsed == null) { onError?.Invoke("AnimationCache library: пустой ответ /prefabs"); yield break; }

			if (_library == null) _library = new Dictionary<string, PrefabEntry>();

			// Мёрж изменившихся entry (replace по slug).
			int changed = 0;
			if (parsed.items != null)
				foreach (var kv in parsed.items) { _library[kv.Key] = kv.Value; changed++; }

			// Удаление: всё, чего нет в all (full-pack семантика removal). all шлётся всегда.
			if (parsed.all != null)
			{
				var keep = new HashSet<string>(parsed.all);
				var toRemove = new List<string>();
				foreach (var slug in _library.Keys)
					if (!keep.Contains(slug)) toRemove.Add(slug);
				foreach (var slug in toRemove) _library.Remove(slug);
			}

			_manifest.prefab_version = parsed.version;
			Debug.Log("AnimationCache: библиотека синхронизирована (since=" + since + "), изменено " + changed + ", всего " + _library.Count);
			SaveLibrary(gameId);
			SaveManifest(gameId);
		}

		// Резолв action → имя клипа для данного prefab с учётом направления (angle).
		// Возвращает (clipName, flipX, clipAngle). flipX=true если clip получен через h_mirror
		// (горизонтальное зеркало). clipAngle — НАРИСОВАННЫЙ угол выбранного клипа (ActionBinding.angle,
		// 0=вправо), null для клипа без направления (angle==null). Зеркало в угол НЕ входит — экранное
		// направление флипнутого клипа (180 − clipAngle) считает потребитель (WeaponMount) по знаку
		// lossyScale.x. Ракурсов может быть меньше, чем направлений: forward «прилипает» к ближайшему
		// существующему клипу, поэтому фактический ракурс тела — clipAngle, а не forward.
		// null clipName если: library ещё не загружена, prefab неизвестен, у prefab нет actions, маппинга на action нет.
		// Вызывающий (EntityModel.SetData) делает fallback на action как имя клипа при null.
		public static (string clipName, bool flipX, int? clipAngle) GetClipName(
			string prefab, string action, float forwardX, float forwardY)
		{
			if (_library == null || string.IsNullOrEmpty(prefab)) return (null, false, null);
			if (!_library.TryGetValue(prefab, out PrefabEntry p)) return (null, false, null);
			if (p.IsImage) return (null, false, null);
			if (p.actions == null) return (null, false, null);

			float targetAngle = Mathf.Atan2(forwardY, forwardX) * Mathf.Rad2Deg;
			if (targetAngle < 0) targetAngle += 360f;

			// Один проход по плоскому списку с фильтром по action (slug повторяется по клипу на направление).
			// angle==null — клип без направления (прежний ключ ""); направленные (angle!=null) конкурируют за
			// ближайший угол к ракурсу. matchCount отличает «единственная привязка, и она без направления»
			// (тогда h_mirror-флип по forwardX) от «есть направленные, best не нашёлся → fallback на
			// без-направления БЕЗ флипа» — поведение прежней словарной формы 1:1.
			int matchCount = 0;
			string noDirClip = null;   // первый клип без направления (angle==null), если есть
			bool hasNoDir = false;
			string bestClip = null;
			float bestDist = 360f;
			bool bestFlip = false;
			int? bestAngle = null;

			foreach (var b in p.actions)
			{
				if (b == null || b.action != action) continue;
				matchCount++;

				if (b.angle == null)
				{
					if (!hasNoDir) { noDirClip = b.clip; hasNoDir = true; }
					continue;
				}
				int clipAngle = b.angle.Value;

				float dist = Mathf.Abs(Mathf.DeltaAngle(targetAngle, clipAngle));
				// При РАВНОЙ дистанции незеркальный кандидат побеждает уже выбранного зеркального: у клипа
				// своей стороны свой набор кадров (руки разведены), а зеркало соседнего клипа несёт те же
				// кости — предмет остался бы в той же руке и на той же стороне тела (WeaponMount ищет точку
				// крепления по кости кадра, глубину — по z якоря). Собственный клип стороны — единственный
				// способ развести руки, поэтому тай-брейк в его пользу. Порядком p.actions решать нельзя:
				// его задаёт сервер (порядок создания привязок), контент-мейкеру он не виден.
				if (dist < bestDist || (dist == bestDist && bestFlip))
				{
					bestDist = dist;
					bestClip = b.clip;
					bestFlip = false;
					bestAngle = clipAngle;
				}

				if (p.h_mirror)
				{
					// h_mirror = горизонтальное зеркало (flipX, лево↔право). Клип, снятый под facing-углом
					// clipAngle, после flipX смотрит под (180 - clipAngle): право(0)↔лево(180), а верх(90)/низ(270)
					// остаются на месте. НЕЛЬЗЯ (360-clipAngle) — это вертикальное зеркало, оно меняет верх↔низ:
					// тогда «Front - Walking» (270) ложно считался бы подходящим под взгляд вверх (90), хотя
					// flipX кадр по вертикали не переворачивает → существо шло бы вверх лицом к камере.
					int mirrorAngle = (180 - clipAngle + 360) % 360;
					float mirrorDist = Mathf.Abs(Mathf.DeltaAngle(targetAngle, mirrorAngle));
					// Строго ближе: при равной дистанции зеркало уступает уже выбранному незеркальному
					// кандидату (тай-брейк выше). Зеркало берётся, только когда своего клипа нет вовсе
					// либо он дальше по углу.
					if (mirrorDist < bestDist)
					{
						bestDist = mirrorDist;
						bestClip = b.clip;
						bestFlip = true;
						bestAngle = clipAngle;
					}
				}
			}

			if (matchCount == 0) return (null, false, null);

			// Единственная привязка и она без направления = clip без направления. Если h_mirror разрешён —
			// зеркалим по X при взгляде влево; иначе клип статичен.
			if (matchCount == 1 && hasNoDir)
				return (noDirClip, p.h_mirror && forwardX < 0, null);

			// Fallback на без-направления если направленного клипа не нашли
			if (bestClip == null && hasNoDir)
				return (noDirClip, false, null);

			return (bestClip, bestFlip, bestAngle);
		}

		// Wrapper над GetSprite: на любой сбой (LoadImage / отсутствие файла) инвалидирует битый кеш
		// (удаляет PNG и сбрасывает archive_last_modified — иначе следующий sync получит 304 и файл
		// не перекачается) и бросает Exception с контекстом. Вызыватель оборачивает в try/catch и
		// сам решает что делать (обычно — ConnectController.Error + оставить sprite=null).
		public static Sprite TryGetSprite(int gameId, string fileName)
		{
			try { return GetSprite(gameId, fileName); }
			catch (Exception ex)
			{
				if (!string.IsNullOrEmpty(fileName))
				{
					string path = Path.Combine(ImagesPath(gameId), fileName);
					// Причину неудачного сноса называем вслух: без неё следующий заход находит тот же битый
					// файл, снова падает на нём и снова молча не может его снять.
					try { if (File.Exists(path)) File.Delete(path); }
					catch (Exception drop) { Debug.LogWarning("AnimationCache: битую картинку " + fileName + " не снять: " + drop.Message); }
					_spriteCache.Remove(fileName);
					if (_manifest != null)
					{
						_manifest.archive_last_modified = null;
						SaveManifest(gameId);
					}
				}
				throw new Exception("AnimationCache: битый image '" + fileName + "' удалён из кеша, перекачается на следующем sync — " + ex.Message, ex);
			}
		}

		// Sprite по имени файла ("sha256.ext"): грузится из локального кеша, кешируется в памяти.
		// pivot=(0.5, 0.5) — центр: так картинка центруется на клетке сущности. Надетый предмет свой pivot
		// (хват) задаёт сам, пересоздавая спрайт из этой же текстуры (WeaponMount.Apply).
		public static Sprite GetSprite(int gameId, string fileName)
		{
			if (string.IsNullOrEmpty(fileName)) return null;
			// Unity-объект в словаре может быть уничтожен Resources.UnloadUnusedAssets при переходе сцен
			// (static-ссылка C# живёт, но нативный ресурс снесён). Проверяем через == null и пересоздаём.
			if (_spriteCache.TryGetValue(fileName, out Sprite cached) && cached != null) return cached;

			string path = Path.Combine(ImagesPath(gameId), fileName);
			if (!File.Exists(path))
			{
				throw new Exception("AnimationCache: отсутствует картинка " + fileName + " (архив устарел?)");
			}

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			// LoadImage возвращает false на битых PNG или PNG, которые Unity не умеет парсить
			// (наблюдалось на валидных файлах с большими iTXt-чанками XMP-метаданных от Photoshop).
			// Кидаем — вызыватель решит, удалить файл из кеша / сбросить manifest / показать Error.
			if (!tex.LoadImage(bytes))
				throw new Exception("AnimationCache: Unity.Texture2D.LoadImage не справился с " + fileName + " (" + bytes.Length + " байт)");
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			// PixelsPerUnit должен совпадать с тем, в котором считает размер надетого предмета WeaponMount.Ppu
			// (=100): предмет пересоздаёт спрайт из этой же текстуры, и разный масштаб дал бы разный размер.
			// SpriteMeshType.Tight — чтобы Sprite.bounds (и SpriteRenderer.bounds) отсекали прозрачные поля PNG.
			// Критично для нормализации размера: без этого предмет с «воздухом» вокруг контента в своём
			// PNG-е измерялся бы завышенными bounds и выходил бы мельче остальных.
			// Рендеринг FullRect vs Tight отличается только числом треугольников меша — визуально идентично.
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.Tight);
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_spriteCache[fileName] = s;
			Debug.Log("AnimationCache: спрайт " + fileName + " загружен с диска");
			return s;
		}

		// Все имена префабов игры (из /prefabs). Используется для создания GameObject'ов по префабам.
		// Контракт по _library тот же что у GetPrefabSize — вызывать только после SyncAll, иначе exception:
		// пустая коллекция замаскировала бы гонку загрузки как «сервер не прислал префабы».
		public static IEnumerable<string> GetPrefabs()
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabs вызван до SyncAll (_library == null). Вызывайте только после завершения SigninController.LoadMain.");
			return _library.Keys;
		}

		// true если Prefab с таким именем существует в серверном списке.
		// Контракт по _library тот же что у GetPrefabSize — вызывать только после SyncAll, иначе exception
		// (тихий false замаскировал бы гонку загрузки как «prefab неизвестен» → сущность без визуала).
		// false для реально отсутствующего prefab'а — это и есть назначение метода (не нарушение).
		public static bool HasPrefab(string name)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.HasPrefab вызван до SyncAll (_library == null). prefab=" + name + ". Вызывайте только после завершения SigninController.LoadMain.");
			return _library.ContainsKey(name);
		}

		// Имя файла картинки (sha256.ext) если prefab — image-only, иначе null.
		// Контракт по _library тот же что у GetPrefabSize — вызывать только после SyncAll, иначе exception.
		// Зовётся из ApplyVisualPrefab (путь применения world-визуала, строго после SyncAll); тихий null
		// замаскировал бы timing-баг как «сущность без визуала» (соседняя ветка HasPrefab тоже вернула бы false).
		// null для prefab'а со скелетом (не image) — легитимный ответ, вызывающий идёт в ветку HasPrefab.
		public static string GetPrefabImage(string name)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabImage вызван до SyncAll (_library == null). prefab=" + name + ". Вызывайте только после завершения SigninController.LoadMain.");
			return _library.TryGetValue(name, out PrefabEntry e) ? e.ImageFile : null;
		}

		// true если у prefab'а есть привязанная скелетная анимация (не image и animation != 0).
		// false и для image-prefab'а, и для kind-only prefab'а (без графики, существует только чтобы
		// донести kind): у обоих скелета нет — клиент его не собирает, сущность остаётся
		// на fallback-визуале Resources-префаба (Prefabs/{kind}).
		// Контракт по _library тот же что у GetPrefabSize — вызывать только после SyncAll, иначе exception.
		public static bool HasAnimation(string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.HasAnimation вызван до SyncAll (_library == null). prefab=" + prefab + ". Вызывайте только после завершения SigninController.LoadMain.");
			return !string.IsNullOrEmpty(prefab) && _library.TryGetValue(prefab, out PrefabEntry e) && !e.IsImage && e.animation != 0;
		}

		// Slug вида (kind) для prefab'а из library. Сервер выводит kind из prefab'а и кладёт в каждый entry
		// /prefabs (в пакетах сущностей kind не едет). Используется UpdateController как имя
		// Resources-префаба (Prefabs/{kind}) при спавне сущности.
		// Строгий контракт (как GetPrefabSize): kind резолвится только из реального prefab'а полного пакета,
		// поэтому любое отсутствие — это баг, а не «значение не задано», и мы падаем громко:
		//   - _library == null → вызов до SyncAll (баг тайминга, тот же что у GetPrefabSize);
		//   - prefab пуст / отсутствует в _library / kind у entry пуст → нарушение целостности данных
		//     (сервер обязан класть kind в каждый entry /prefabs). Тихий fallback замаскировал бы это.
		public static string GetPrefabKind(string prefabSlug)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabKind вызван до SyncAll (_library == null). prefab=" + prefabSlug + ". Вызывайте только после завершения SigninController.LoadMain.");
			if (string.IsNullOrEmpty(prefabSlug) || !_library.TryGetValue(prefabSlug, out PrefabEntry e) || string.IsNullOrEmpty(e.kind))
				throw new InvalidOperationException("AnimationCacheService.GetPrefabKind: kind не резолвится для prefab='" + prefabSlug + "' (prefab пуст, отсутствует в library, или у entry пустой kind). Сервер обязан класть kind в каждый entry /prefabs.");
			return e.kind;
		}

		// Готовый Sprite иконки для image-prefab. null — если prefab не image (animation
		// или отсутствует в library) или картинка битая (битый кеш чистится TryGetSprite,
		// перекачается на следующем sync). Используется UI-кодом (Spell, Item) — они передают
		// BaseController.GAME_ID (public static, глобальный конфиг проекта).
		// Контракт: вызывать только после SigninController.LoadMain (т.е. _library != null).
		public static Sprite GetPrefabSprite(int gameId, string prefab)
		{
			if (_library == null)
				throw new InvalidOperationException("AnimationCacheService.GetPrefabSprite вызван до SyncAll (_library == null). prefab=" + prefab);
			string imageFile = GetPrefabImage(prefab);
			if (imageFile == null) return null;
			try { return TryGetSprite(gameId, imageFile); }
			catch (Exception ex) { Debug.LogWarning("GetPrefabSprite '" + prefab + "': " + ex.Message); return null; }
		}
	}
}
