namespace Mmogick
{
    /// <summary>
    /// Атака заклинанием (дальнобойная магия)
    /// </summary>
    public class BoltResponse : MeleeResponse
    {
        public new const string GROUP = "fight/bolt";

        public override string group
        {
            get { return GROUP; }
        }

        public string magic;
    }
}
