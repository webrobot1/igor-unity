namespace Mmogick
{
    /// <summary>
    /// Ближняя атака (рукопашная)
    /// </summary>
    public class MeleeResponse : Response
    {
        public const string GROUP = "fight/melee";

        public override string group
        {
            get { return GROUP; }
        }

        public double? x;
        public double? y;
        public double? z;

        public string target;
    }
}
