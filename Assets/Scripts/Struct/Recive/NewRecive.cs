using System.Collections.Generic;

namespace MyFantasy
{
    /// <summary>
    /// —cтруктура получаемых данных данных
    /// </summary>
    [System.Serializable]
    public class NewRecive<P, E, O> : Recive<NewPlayerRecive, NewEnemyRecive, NewObjectRecive> where P : ObjectRecive where E : ObjectRecive where O : ObjectRecive
    {

    }
}