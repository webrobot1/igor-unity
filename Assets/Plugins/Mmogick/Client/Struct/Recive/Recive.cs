using System.Collections.Generic;

namespace Mmogick
{
    /// <summary>
    /// Пакет сервера целиком: служебные поля конверта плюс сам мир. Служебные приходят от
    /// <see cref="ReciveEnvelope"/> — тот же пакет, прочитанный на меньшую глубину сетевым потоком,
    /// и держать их вторым списком нельзя: разъехавшись, два списка молча разойдутся и с сервером.
    /// </summary>
    [System.Serializable]
    public class Recive<P, E> : ReciveEnvelope where P : EntityRecive where E : EntityRecive
    {
        public Dictionary<int, MapRecive<P, E>> world;

        /// <summary>
        ///  с какой стороны какой номер карты (мы мо этим номерам при пеерходе на другую карту смещаем карты что бы не запрашивать их снова)
        /// </summary>
        public Dictionary<int, MapSide> sides;
    }
}
