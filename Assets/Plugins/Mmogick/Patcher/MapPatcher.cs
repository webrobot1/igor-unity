using System;
using System.Collections;

namespace Mmogick
{
	// Тонкая обёртка-«контейнер результата» над TileCacheService.GetMap.
	public class MapPatcher
	{
		public string error;
		public string result;  // JSON карты

		// Кеш карты ведёт TileCacheService (If-Modified-Since, persistentDataPath/games/{gameId}/maps).
		public static IEnumerator Get(string server, int game_id, string token, int map_id, Action<MapPatcher> callback)
		{
			MapPatcher patcher = new MapPatcher();
			yield return TileCacheService.GetMap(server, game_id, map_id, token, (string json, string error) =>
			{
				patcher.result = json;
				patcher.error  = error;
			});
			callback(patcher);
		}
	}
}
