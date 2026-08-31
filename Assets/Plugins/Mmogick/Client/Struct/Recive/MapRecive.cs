using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// Структура получаемых данных мира определённой карты.
    /// Групп две: player (игроки) и entity (все остальные). Вид конкретного entity (kind) в пакете не едет —
    /// клиент резолвит его из EntityRecive.prefab через AnimationCacheService.GetPrefabKind (справочник /prefabs).
    /// </summary>
    [System.Serializable]
    public class MapRecive<P, E> where P : EntityRecive where E : EntityRecive
    {
        public Dictionary<string, P> player;
        public Dictionary<string, E> entity;
    }
}
