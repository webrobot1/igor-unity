namespace Mmogick
{
    /// <summary>
    /// Лечебное заклинание
    /// </summary>
    public class HealResponse : Response
    {
        public const string GROUP = "magic/heal";

        public override string group
        {
            get { return GROUP; }
        }

        public string spell;

        /// <summary>
        /// ключ цели. null — сервер лечит самого заклинателя
        /// </summary>
        public string target;
    }
}
