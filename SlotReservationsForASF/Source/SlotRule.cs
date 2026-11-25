using Verse;

namespace SlotReservations
{
    /// <summary>One rule: reserve <see cref="count"/> stack slots for things allowed by <see cref="filter"/>.</summary>
    public class SlotRule : IExposable
    {
        public int count = 1;
        public ThingFilter filter = new ThingFilter();

        public void ExposeData()
        {
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Deep.Look(ref filter, "filter");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && filter == null) filter = new ThingFilter();
        }

        public SlotRule Copy()
        {
            var r = new SlotRule { count = count };
            r.filter.CopyAllowancesFrom(filter);
            return r;
        }

        public string Label => filter.AllowedDefCount == 0 ? (string)"SR_Nothing".Translate() : filter.Summary;
    }
}
