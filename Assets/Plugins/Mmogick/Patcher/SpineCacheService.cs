using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Networking;

namespace Mmogick
{
	/// <summary>
	/// Кеш скелетов Spine игры: пакет «скелет + описание атласа + якоря экипировки» с сервера и сборка из него
	/// рантайм-скелета для сцены.
	///
	/// Канал: GET /animation/patch/{gameId}/{token}/animations/{id}/spine — по варианту скелета отдаёт сам
	/// скелет и описание атласа (сжатыми) плюс якоря экипировки. Свежесть — та же, что у списка /animations:
	/// версии ведёт <see cref="AnimationCacheService"/>, и пакет качается, когда версия разошлась.
	///
	/// Картинки берутся из ОБЩЕГО кеша анимаций (images/{sha256}.{ext}): страницы атласа названы отпечатком
	/// картинки с расширением, потому переводной таблицы имён тут нет вовсе — атлас разрешается прямо в кеше.
	///
	/// Собранные скелет и атлас — объекты Unity, созданные кодом: остановка игры их уничтожает, а статический
	/// кеш переживает её (перезагрузка домена в проекте выключена). Оттого выдача проверяет живость каждого
	/// Unity-оператором и при промахе собирает заново.
	/// </summary>
	public static class SpineCacheService
	{
		// Расширение файла кеша пакета.
		private const string PACKAGE_EXT = ".spine.json";

		// Собранные скелеты: «идентификатор анимации/имя варианта» → готовый скелет.
		private static readonly Dictionary<string, SkeletonDataAsset> _skeletons = new Dictionary<string, SkeletonDataAsset>();

		// Текстуры страниц: имя файла картинки → текстура. Общие у всех скелетов игры — одна картинка
		// служит страницей многим.
		private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

		// Однократные клипы собранного скелета: сам скелет их не несёт — повтор у Spine задаёт запускающий.
		private static readonly Dictionary<SkeletonDataAsset, HashSet<string>> _once =
			new Dictionary<SkeletonDataAsset, HashSet<string>>();

		// Разобранные якоря экипировки: «идентификатор анимации/имя варианта» → слот → якоря. Разбор пакета
		// читает с диска и распаковывает файл целиком (скелет и атлас КАЖДОГО варианта), а якоря спрашивают
		// по надетому предмету на каждом носителе — без памятки один и тот же файл разбирался бы на каждом.
		private static readonly Dictionary<string, Dictionary<string, List<SlotAnchor>>> _slots =
			new Dictionary<string, Dictionary<string, List<SlotAnchor>>>();



		/// <summary>Пакет скелетов анимации, как его отдаёт сервер.</summary>
		private class SpinePackage
		{
			/// <summary>Имя варианта скелета → его скелет, атлас и якоря.</summary>
			public Dictionary<string, SpineEntity> entity;

			/// <summary>Версия анимации у сервера — ею же помечен список /animations.</summary>
			public long updated;
		}

		private class SpineEntity
		{
			/// <summary>Скелет в формате Spine, сжатый и в base64.</summary>
			public string skeleton;

			/// <summary>Описание атласа, сжатое и в base64.</summary>
			public string atlas;

			/// <summary>Слот экипировки → его якоря (у слота их несколько — по кости на ракурс).</summary>
			public Dictionary<string, List<SlotAnchor>> object_slot;

			/// <summary>Клипы, играющиеся ОДИН раз (смерть, удар): у Spine повтор задаёт запускающий.</summary>
			public List<string> clip_once;
		}

		/// <summary>
		/// Якорь слота экипировки: кость скелета и посадка предмета на ней. Состав полей — весь, что шлёт
		/// сервер: разбор серверного пакета в редакторе строгий, и незаявленное поле роняет весь пакет.
		/// </summary>
		public class SlotAnchor
		{
			/// <summary>Имя кости скелета, на которой висит предмет.</summary>
			public string bone;

			/// <summary>
			/// Слот скелета, которым якорь задан: он несёт и кость, и место в порядке отрисовки — держатель
			/// сервер ставит сразу за ним. Клиенту нужен составом пакета: разбор строгий.
			/// </summary>
			public string anchor;

			/// <summary>Слот экипировки, которому якорь принадлежит; у ключа набора он же.</summary>
			public string slot;

			public float offsetX;
			public float offsetY;

			/// <summary>Угол доворота предмета за костью, в градусах; поворота нет — 0.</summary>
			public float angle;

			/// <summary>
			/// Слот скелета, в который ставится кусок надетого предмета. Держатели стоят в скелете пустыми,
			/// имя им задаёт сервер — клиент его не собирает.
			/// </summary>
			public string holder;

			public float scale = 1f;
		}

		/// <summary>
		/// Скелет варианта для этого префаба: из кеша, а нет — качается с сервера и кладётся в кеш.
		/// Ошибка приходит вторым аргументом; скелет при ней пуст.
		/// </summary>
		public static IEnumerator GetSkeleton(string host, int gameId, string prefab, string token,
			Action<SkeletonDataAsset, string> callback)
		{
			int animationId = AnimationCacheService.GetPrefabAnimation(prefab);
			string entity = AnimationCacheService.GetPrefabEntity(prefab);
			if (animationId == 0 || string.IsNullOrEmpty(entity))
			{
				// Легитимное отсутствие: у префаба нет скелета вовсе (набор картинок либо только вид).
				callback(null, null);
				yield break;
			}

			string failure = null;
			string key = animationId + "/" + entity;
			if (_skeletons.TryGetValue(key, out var cached) && cached != null)
			{
				callback(cached, null);
				yield break;
			}

			string packageFile = PackageFile(gameId, animationId);
			if (!File.Exists(packageFile))
			{
				// Обычно пакет уже лежит: его качает синхронизация перед входом. Сюда попадают лишь записи,
				// появившиеся после неё.
				yield return Fetch(host, gameId, animationId, token, error => failure = error);
				if (failure != null) { callback(null, failure); yield break; }
			}

			SkeletonDataAsset asset;
			failure = Build(gameId, packageFile, entity, out asset);
			if (failure != null)
			{
				// Кеш мог протухнуть (картинки архива сменились) — сносим, следующий заход перекачает.
				DeletePackage(packageFile);
				callback(null, failure);
				yield break;
			}

			_skeletons[key] = asset;
			callback(asset, null);
		}

		/// <summary>
		/// Пакет анимации на диске: нет — качается. Зовётся предзагрузкой перед входом в игру, чтобы к
		/// спавну существа файл уже лежал: иначе десяток существ одного вида заводит десяток запросов за
		/// одним и тем же файлом. Свежесть решает вызывающий (он ведёт отметки версий) — снятый им пакет
		/// тут и качается заново.
		/// </summary>
		public static IEnumerator Ensure(string host, int gameId, int animationId, string token,
			Action<string> onError = null)
		{
			if (animationId == 0 || File.Exists(PackageFile(gameId, animationId))) yield break;
			yield return Fetch(host, gameId, animationId, token, onError);
		}

		/// <summary>Запрос пакета и запись его в кеш. Ошибка уходит вызывающему, файл при ней не появляется.</summary>
		private static IEnumerator Fetch(string host, int gameId, int animationId, string token, Action<string> onError)
		{
			string url = "http://" + host + "/animation/patch/" + gameId + "/" + token
				+ "/animations/" + animationId + "/spine";
			UnityWebRequest request = UnityWebRequest.Get(url);
			yield return request.SendWebRequest();

			if (request.result != UnityWebRequest.Result.Success)
			{
				onError?.Invoke("SpineCache " + animationId + ": " + request.error);
				request.Dispose();
				yield break;
			}

			string body = request.downloadHandler.text;
			request.Dispose();
			try { File.WriteAllText(PackageFile(gameId, animationId), body); }
			catch (Exception ex) { onError?.Invoke("SpineCache запись кеша: " + ex.Message); }

			// Файл сменился — разобранное из ПРЕЖНЕГО в памяти соврёт. Память переживает остановку игры
			// (перезагрузка домена выключена), потому чистим её тут, у самой записи файла: скачивание —
			// единственный путь, которым содержимое пакета меняется под уже разобранной памяткой.
			Forget(animationId);
		}

		/// <summary>
		/// Якоря экипировки варианта скелета: слот → его якоря. Пусто — у скелета якорей нет; null — пакета
		/// ещё нет в кеше (скелет не качали).
		/// </summary>
		public static Dictionary<string, List<SlotAnchor>> GetSlots(int gameId, int animationId, string entity)
		{
			string key = animationId + "/" + entity;
			if (_slots.TryGetValue(key, out var memo))
				return memo;

			string packageFile = PackageFile(gameId, animationId);
			if (!File.Exists(packageFile))
				return null;

			var package = Read(packageFile, out _);
			if (package == null || package.entity == null || !package.entity.TryGetValue(entity, out var found))
				return null;

			memo = found.object_slot ?? new Dictionary<string, List<SlotAnchor>>();
			_slots[key] = memo;
			return memo;
		}

		/// <summary>
		/// Повторяется ли клип скелета. Незацикленные перечисляет сервер: у формата Spine повтор — решение
		/// запускающего, в скелете его нет. Скелета нет в кеше — клип считаем зацикленным: так ведёт себя
		/// ходьба и стойка, которых у сущности большинство.
		/// </summary>
		public static bool Loops(SkeletonDataAsset asset, string clip)
			=> asset == null || !_once.TryGetValue(asset, out var once) || !once.Contains(clip);

		/// <summary>
		/// Забыть разобранное по анимации, файл пакета оставив: зовётся при его перекачке. Отдельно от
		/// <see cref="Drop"/> — тот снимает и сам файл, а после скачивания снимать нечего.
		/// </summary>
		private static void Forget(int animationId)
		{
			string prefix = animationId + "/";
			var stale = new List<string>();
			foreach (var pair in _skeletons)
				if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(pair.Key);
			foreach (var key in stale)
			{
				// Перечень однократных клипов ключуется САМИМ скелетом: снятый из выдачи адресовать больше
				// нечем, и без этой строки словарь держал бы забытый скелет до конца сеанса (память переживает
				// остановку игры). Ключ снимаем по ссылке, без Unity-проверки на живость: уничтоженный объект
				// она отсекает, а запись словаря он при этом держит.
				if (_skeletons.TryGetValue(key, out var asset) && !ReferenceEquals(asset, null))
					_once.Remove(asset);

				_skeletons.Remove(key);
			}

			stale.Clear();
			foreach (var pair in _slots)
				if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) stale.Add(pair.Key);
			foreach (var key in stale) _slots.Remove(key);
		}

		/// <summary>
		/// Забыть всё разобранное: зовёт сброс кеша анимаций, сносящий сами файлы пакетов. Память переживает
		/// остановку игры, и без этого выдача отвечала бы по снятым файлам до конца сеанса.
		/// </summary>
		public static void Reset()
		{
			_skeletons.Clear();
			_slots.Clear();
			_once.Clear();
			_textures.Clear();
		}

		/// <summary>Снять кеш пакета анимации — версия разошлась с серверной.</summary>
		public static void Drop(int gameId, int animationId)
		{
			string packageFile = PackageFile(gameId, animationId);
			if (File.Exists(packageFile))
				DeletePackage(packageFile);

			Forget(animationId);
		}

		/// <summary>
		/// Снять файл пакета. Причину неудачи называем вслух: снаружи видно лишь «пакет не разбирается», а
		/// следующий заход упрётся в тот же файл и повторит тот же отказ — и так каждый вход.
		/// </summary>
		private static void DeletePackage(string packageFile)
		{
			try { File.Delete(packageFile); }
			catch (Exception ex) { Debug.LogWarning("SpineCache: пакет " + packageFile + " не снят: " + ex.Message); }
		}

		private static string PackageFile(int gameId, int animationId)
			=> Path.Combine(AnimationCacheService.StructuresPath(gameId), animationId + PACKAGE_EXT);

		/// <summary>
		/// Разбор пакета. Помеха возвращается ТЕКСТОМ: без неё «пакет не разбирается» не отличить от
		/// «файла нет», а причина разбора (незаявленное поле, оборванная запись) видна только в исключении.
		/// </summary>
		private static SpinePackage Read(string packageFile, out string failure)
		{
			failure = null;
			try
			{
				var package = JsonConvert.DeserializeObject<SpinePackage>(
					File.ReadAllText(packageFile),
					new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
				if (package == null) failure = "пустой пакет";
				return package;
			}
			catch (Exception ex) { failure = ex.Message; return null; }
		}

		/// <summary>
		/// Сборка скелета из пакета: атлас на страницах-картинках кеша плюс сам скелет. Возвращает описание
		/// помехи либо null, когда собралось.
		/// </summary>
		private static string Build(int gameId, string packageFile, string entity, out SkeletonDataAsset asset)
		{
			asset = null;

			var package = Read(packageFile, out string failure);
			if (package == null || package.entity == null)
				return "SpineCache: пакет " + packageFile + " не разбирается: " + (failure ?? "нет варианта скелета");
			if (!package.entity.TryGetValue(entity, out var found))
				return "SpineCache: варианта «" + entity + "» в пакете нет";

			string skeletonJson = Unpack(found.skeleton);
			string atlasText = Unpack(found.atlas);
			if (skeletonJson == null || atlasText == null)
				return "SpineCache: скелет либо атлас варианта «" + entity + "» не распаковываются";

			var textures = new List<Texture2D>();
			foreach (string page in Pages(atlasText))
			{
				Texture2D texture = Texture(gameId, page);
				if (texture == null)
					return "SpineCache: страницы «" + page + "» нет в кеше картинок";
				textures.Add(texture);
			}

			// Материал-образец: рантайм-атлас копирует его на каждую страницу, подставляя свою текстуру.
			var source = new Material(Shader.Find("Spine/Skeleton"));
			var atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(new TextAsset(atlasText), textures.ToArray(), source, true);
			// Масштаб единиц оставляем как есть: высоту тела в клетку приводит сборка визуала, а её делитель
			// сервер объявляет в тех же единицах, в каких записан скелет.
			asset = SkeletonDataAsset.CreateRuntimeInstance(new TextAsset(skeletonJson), atlasAsset, true, 1f);
			_once[asset] = new HashSet<string>(found.clip_once ?? new List<string>());

			return asset.GetSkeletonData(true) != null ? null : "SpineCache: скелет варианта «" + entity + "» не собрался";
		}

		/// <summary>Имена страниц атласа: строки с расширением картинки, стоящие без отступа.</summary>
		private static IEnumerable<string> Pages(string atlasText)
		{
			foreach (string raw in atlasText.Split('\n'))
			{
				if (raw.Length == 0 || raw[0] == ' ' || raw[0] == '\t') continue;
				string line = raw.Trim();
				if (line.Length == 0 || line.IndexOf(':') >= 0) continue;
				if (Path.HasExtension(line)) yield return line;
			}
		}

		/// <summary>
		/// Текстура страницы из общего кеша картинок. Имя текстуры — без расширения: атлас ищет страницу
		/// именно по нему, и с расширением совпадения не будет вовсе.
		/// </summary>
		private static Texture2D Texture(int gameId, string page)
		{
			if (_textures.TryGetValue(page, out var cached) && cached != null)
				return cached;

			string file = Path.Combine(AnimationCacheService.ImagesDirPath(gameId), page);
			if (!File.Exists(file))
				return null;

			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			texture.LoadImage(File.ReadAllBytes(file));
			texture.name = Path.GetFileNameWithoutExtension(page);
			_textures[page] = texture;
			return texture;
		}

		/// <summary>Строка пакета: сжата gzip и передана в base64.</summary>
		private static string Unpack(string packed)
		{
			if (string.IsNullOrEmpty(packed)) return null;
			try
			{
				byte[] raw = Convert.FromBase64String(packed);
				using (var source = new MemoryStream(raw))
				using (var gzip = new GZipStream(source, CompressionMode.Decompress))
				using (var target = new MemoryStream())
				{
					gzip.CopyTo(target);
					return Encoding.UTF8.GetString(target.ToArray());
				}
			}
			catch (Exception) { return null; }
		}
	}
}
