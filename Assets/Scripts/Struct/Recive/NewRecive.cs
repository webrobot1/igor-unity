using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// cтруктура получаемых данных данных. сюда в теории можно добавить какие то поля котоыре будут расылать события кроме стандартных (системные поля типа action pingf и ошибк + и 3 главных словаря - игроки, враги и объекты)
    /// </summary>
    [System.Serializable]
    public class NewRecive<P, E, O> : Recive<PlayerRecive, EnemyRecive, ObjectRecive> where P : EntityRecive where E : EntityRecive where O : EntityRecive
    {

    }
}