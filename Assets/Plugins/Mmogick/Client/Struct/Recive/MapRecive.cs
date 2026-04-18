using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// Структура получаемых данных мира определённой карты.
    /// После унификации Enemy/Animal/Objects в единую Entity используются две группы:
    /// player (игроки) и entity (все остальные). Вид конкретного entity — в EntityRecive.kind.
    /// </summary>
    [System.Serializable]
    public class MapRecive<P, E> where P : EntityRecive where E : EntityRecive
    {
        public Dictionary<string, P> player;
        public Dictionary<string, E> entity;
    }
}
