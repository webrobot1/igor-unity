using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Картинки локального кеша игры, разобранные в спрайты: чтение файла с диска, память между запросами и
	/// снос записи, на которой разбор сорвался. Таких кешей у игры два — графика тайлов карты и графика
	/// анимаций, — и различаются они каталогом файлов, хвостом имени файла при ключе-отпечатке и конвенцией
	/// сборки спрайта (точка отсчёта, пикселей на единицу, форма меша). Всё это владелец объявляет при
	/// создании кеша: одно и то же чтение под разные конвенции.
	///
	/// Память НЕ общая на все кеши: у каждого своя, и снимается она вместе со своим архивом (см. Clear) —
	/// общий словарь чистился бы за оба сразу.
	/// </summary>
	public class SpriteCache
	{
		// Имя владельца в тексте отказа: по нему в журнале видно, чей кеш оказался битым.
		private readonly string _owner;

		// Каталог файлов этого кеша.
		private readonly Func<int, string> _folder;

		// Хвост имени файла, которого нет в ключе: кеш тайлов адресует картинку голым отпечатком, а хранит
		// её как «отпечаток.png». Ключ остаётся тем, чем его называет владелец, — он же идёт в текст отказа.
		private readonly string _suffix;

		// Точка отсчёта спрайта (Sprite.Create pivot).
		private readonly Vector2 _pivot;

		// Пикселей графики на единицу мира. Правило, а не число: у тайла оно считается от самой текстуры
		// (тайл занимает ровно клетку, какого бы разрешения он ни был), у графики анимаций — константа.
		private readonly Func<Texture2D, float> _pixelsPerUnit;

		// Форма меша спрайта (Sprite.Create meshType).
		private readonly SpriteMeshType _meshType;

		// Что сделать, когда картинка оказалась битой: снять отметку свежести архива у владельца — иначе
		// следующий sync получит 304, и снесённый файл не перекачается.
		private readonly Action<int> _onBroken;

		private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

		public SpriteCache(string owner, Func<int, string> folder, string suffix, Vector2 pivot,
			Func<Texture2D, float> pixelsPerUnit, SpriteMeshType meshType, Action<int> onBroken)
		{
			_owner = owner;
			_folder = folder;
			_suffix = suffix;
			_pivot = pivot;
			_pixelsPerUnit = pixelsPerUnit;
			_meshType = meshType;
			_onBroken = onBroken;
		}

		/// <summary>
		/// Забыть разобранное: зовут сброс кеша, загрузка кеша другой игрой и приход нового архива, где картинки
		/// могли смениться.
		/// Снятой ссылки движку мало — спрайт и его текстура созданы кодом и помечены DontUnloadUnusedAsset,
		/// то есть выгрузка неиспользуемого их не заберёт: без явного сноса они держали бы свои пиксели до
		/// конца сеанса (память переживает остановку игры — перезагрузка домена в проекте выключена).
		/// Текстуру сносим отдельно: спрайт ею не владеет.
		///
		/// Кто держит выданный отсюда спрайт, обязан пережить снос — проверять живость и рисовать заново
		/// (см. MapDecodeModel.getTileAsset). Зовут этот снос вне игры (синхронизация перед входом) либо
		/// на выходе из неё (ResetCache вместе с ConnectController.Error).
		/// </summary>
		public void Clear()
		{
			foreach (Sprite sprite in _cache.Values)
			{
				if (sprite == null) continue;

				Texture2D texture = sprite.texture;
				UnityEngine.Object.Destroy(sprite);
				if (texture != null) UnityEngine.Object.Destroy(texture);
			}

			_cache.Clear();
		}

		/// <summary>
		/// Спрайт по ключу. На любой сбой (файла нет, разбор картинки не удался) инвалидирует битый кеш —
		/// удаляет файл и снимает у владельца отметку свежести архива — и бросает Exception с контекстом.
		/// Вызыватель оборачивает в try/catch и сам решает что делать (обычно — ConnectController.Error
		/// плюс оставить sprite=null).
		/// </summary>
		public Sprite Get(int gameId, string key)
		{
			try { return Load(gameId, key); }
			catch (Exception ex)
			{
				string outcome = "удалена из кеша, перекачается на следующем sync";

				if (!string.IsNullOrEmpty(key))
				{
					string path = Path.Combine(_folder(gameId), key + _suffix);
					// Причину неудачного сноса называем в тексте отказа вместо обещания перекачки: без неё
					// следующий заход находит тот же битый файл, снова падает на нём и снова не может его снять.
					try { if (File.Exists(path)) File.Delete(path); }
					catch (Exception drop)
					{
						Debug.LogException(drop);
						outcome = "не снята с кеша (" + drop.Message + ")";
					}
					_cache.Remove(key);
					_onBroken(gameId);
				}
				throw new Exception(_owner + ": битая картинка '" + key + "' " + outcome + " — " + ex.Message, ex);
			}
		}

		private Sprite Load(int gameId, string key)
		{
			// Unity-объект в словаре может быть уничтожен Resources.UnloadUnusedAssets при переходе сцен
			// (static-ссылка C# живёт, но нативный ресурс снесён). Проверяем через == null и пересоздаём.
			if (_cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

			string path = Path.Combine(_folder(gameId), key + _suffix);
			if (!File.Exists(path))
				throw new Exception(_owner + ": отсутствует картинка " + key + " (архив устарел?)");

			byte[] bytes = File.ReadAllBytes(path);
			Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			// LoadImage возвращает false на битых PNG и на тех, что Unity не умеет разобрать (наблюдалось на
			// валидных файлах с большими iTXt-чанками XMP-метаданных от Photoshop). Текстура при этом остаётся
			// заготовкой, и картинка тихо отрисовалась бы мусором — потому бросаем: Get снесёт файл из кеша,
			// а вызыватель решит, показывать ли ошибку. Заготовка создана кодом и в кеш не легла: снятой
			// ссылки движку мало, сносим явно.
			if (!tex.LoadImage(bytes))
			{
				UnityEngine.Object.Destroy(tex);
				throw new Exception(_owner + ": Unity.Texture2D.LoadImage не справился с " + key
					+ " (" + bytes.Length + " байт)");
			}
			tex.filterMode = FilterMode.Point;
			tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
			Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), _pivot, _pixelsPerUnit(tex), 0, _meshType);
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_cache[key] = s;
			return s;
		}
	}
}
