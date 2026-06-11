using System;
using System.Collections;

namespace Mmogick
{
	// Тонкая обёртка-«контейнер результата» над AnimationCacheService.GetStructure.
	public class AnimationPatcher
	{
		public string        error;
		public SpriterPacket spriterPacket;  // SCML XML + sha256-список картинок (из локального кеша)

		// Читает структуру (SCML + sha256-маппинг) через AnimationCacheService.GetStructure — из локального кеша структур;
		// при cache-miss лениво докачивает SCML с /animation/patch/{gameId}/{token}/animations/{animationId}.
		// Кеширование: файловый кеш структур + If-Modified-Since для /images.
		// Картинки берутся из локального кеша AnimationCacheService (см. SyncImagesArchive).
		public static IEnumerator Get(string server, int game_id, string token, string prefab, Action<AnimationPatcher> callback)
		{
			AnimationPatcher patcher = new AnimationPatcher();
			yield return AnimationCacheService.GetStructure(server, game_id, prefab, token, (xml, files, error) =>
			{
				patcher.error = error;
				if (error == null)
					patcher.spriterPacket = new SpriterPacket { xml = xml, files = files };
			});
			callback(patcher);
		}
	}
}
