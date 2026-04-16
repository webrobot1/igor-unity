using System;
using System.Collections;

namespace Mmogick
{
	// Тонкая обёртка-«контейнер результата» над AnimationCacheService.GetStructure.
	public class AnimationPatcher
	{
		public string        error;
		public SpriterPacket spriterPacket;  // SCML XML + sha256-список картинок (из локального кеша)

		// Скачивает SCML+sha256-маппинг с /animation/patch/{gameId}/{token}/structure/{prefab} (с ETag-кешированием).
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
