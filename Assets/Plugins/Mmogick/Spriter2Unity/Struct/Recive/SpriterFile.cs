using UnityEngine;

namespace Mmogick
{
    // Файл-картинка из SCML. Сервер отдаёт sha256+ext, реальные байты лежат в локальном кеше
    // (см. AnimationCacheService.SyncImagesArchive). Спрайт берётся методом GetSprite(gameId).
    public class SpriterFile
    {
        public int    folder_id;
        public int    id;
        public string sha256;
        public string ext;

        public Sprite GetSprite(int gameId)
        {
            return AnimationCacheService.GetSprite(gameId, sha256, ext);
        }
    }
}
