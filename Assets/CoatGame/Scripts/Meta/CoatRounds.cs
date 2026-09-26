using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Draws the round at the start of a game, and makes one you have just
    /// played unlikely to come round again immediately.
    ///
    /// **The rarity is by RECENCY, not by lifetime plays.** Counting total
    /// plays reads the same in a sentence and is wrong in practice: a round you
    /// enjoyed forty times would sink to never appearing again and stay there,
    /// so the pool quietly shrinks to whatever you have played least. Here the
    /// penalty is deep immediately after playing and then recovers, so nothing
    /// is ever retired -- it just stops repeating back to back.
    ///
    /// **Determinism.** Pick is a pure function of the history and a seed, with
    /// its own System.Random rather than UnityEngine.Random, so it cannot be
    /// perturbed by anything else drawing a random number that frame. But note
    /// what that does NOT buy: the history is PER PLAYER, so the same seed on
    /// two machines gives two different rounds. Once Fusion is in, the host
    /// draws and replicates the RESULT -- the round id -- not the seed. Same
    /// shape as the observer: one-way, host decides, clients display. See the
    /// networking notes.
    public static class CoatRounds
    {
        [Tooltip("How many rounds it takes for a played round to be fully " +
                 "back in the draw.")]
        public static int Memory = 5;

        /// Weight multiplier for a round played THIS moment ago. Small, but
        /// never zero -- at zero a two-round pool would deadlock the moment
        /// both had been played, and a one-round pool could never draw at all.
        public static float Floor = 0.05f;

        /// How far back the history goes. Only the newest Memory entries can
        /// affect a draw, so this only needs to be comfortably larger.
        public const int HistoryCap = 24;

        // ---- the stock ---------------------------------------------------

        static CoatRoundStock _stock;
        static bool _looked;

        public static CoatRoundStock Stock
        {
            get
            {
                if (!_looked)
                {
                    _stock = Resources.Load<CoatRoundStock>("CoatRoundStock");
                    _looked = true;
                }
                return _stock;
            }
        }

        public static void Forget() { _stock = null; _looked = false; }

        /// Swap in a round list with no asset on disk, for tests.
        public static void UseForTesting(CoatRoundStock s) { _stock = s; _looked = true; }

        public static IReadOnlyList<RoundDef> All =>
            Stock != null && Stock.Rounds != null
                ? (IReadOnlyList<RoundDef>)Stock.Rounds
                : System.Array.Empty<RoundDef>();

        public static RoundDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Id == id) return all[i];
            return null;
        }

        /// The round currently in play. Null before the first draw.
        public static RoundDef Current { get; private set; }

        public static void Clear() => Current = null;

        /// Set the already-drawn round from an authoritative network result.
        /// No new random draw occurs on clients.
        public static void SetCurrent(string id)
        {
            Current = Find(id);
        }


        // ---- weighting ---------------------------------------------------

        /// How many rounds ago this was last played. 0 is the round just gone,
        /// -1 means never.
        ///
        /// `recent` runs oldest first, newest last.
        public static int RoundsAgo(IReadOnlyList<string> recent, string id)
        {
            if (recent == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = recent.Count - 1; i >= 0; i--)
                if (recent[i] == id) return (recent.Count - 1) - i;
            return -1;
        }

        /// The repeat penalty on its own. Floor at zero rounds ago, climbing
        /// back to 1 once Memory rounds have passed.
        public static float RepeatWeight(int ago)
        {
            if (ago < 0) return 1f;                  // never played
            if (Memory <= 0) return 1f;
            if (ago >= Memory) return 1f;            // fully recovered
            float t = ago / (float)Memory;
            return Mathf.Lerp(Mathf.Max(0.0001f, Floor), 1f, t);
        }

        public static float WeightOf(RoundDef d, IReadOnlyList<string> recent)
        {
            if (d == null) return 0f;
            float w = Mathf.Max(0f, d.Weight);
            if (w <= 0f) return 0f;                  // parked by the designer
            return w * RepeatWeight(RoundsAgo(recent, d.Id));
        }

        // ---- drawing -----------------------------------------------------

        /// One uniform value in [0,1) from a seed, well mixed.
        ///
        /// This replaced `new System.Random(seed).NextDouble()`, which was
        /// wrong here in two ways. Taking only the FIRST value out of a Random
        /// seeded from a counter gives results that walk almost linearly as
        /// the seed increments -- measured over 12000 rounds it skewed the
        /// spread from an even 16.7% each to between 8.1% and 26.8%. And the
        /// seed in real use is Environment.TickCount, which increments just
        /// like a counter, so consecutive games would have been correlated too,
        /// not only the test.
        ///
        /// It is also a SplitMix32 finalizer written out rather than a library
        /// call, because System.Random's algorithm is not contractually stable
        /// between runtimes -- and once the host and a client have to agree on
        /// a draw, "random" has to mean the same arithmetic everywhere.
        static double Roll(int seed)
        {
            uint x = (uint)seed + 0x9E3779B9u;
            x = (x ^ (x >> 16)) * 0x21F0AAADu;
            x = (x ^ (x >> 15)) * 0x735A2D97u;
            x ^= x >> 15;
            return x / 4294967296.0;
        }

        /// Pure: same history and same seed always give the same round.
        public static RoundDef Pick(IReadOnlyList<string> recent, int seed)
        {
            var all = All;
            if (all.Count == 0) return null;

            var w = new double[all.Count];
            double total = 0.0;
            for (int i = 0; i < all.Count; i++)
            {
                w[i] = WeightOf(all[i], recent);
                total += w[i];
            }

            // Every round parked at weight 0. Rather than refuse to start a
            // game, fall back to an even draw across them.
            if (total <= 0.0)
                return all[Mathf.Clamp((int)(Roll(seed) * all.Count), 0, all.Count - 1)];

            double roll = Roll(seed) * total;
            for (int i = 0; i < all.Count; i++)
            {
                roll -= w[i];
                if (roll <= 0.0) return all[i];
            }
            return all[all.Count - 1];   // only reachable on float slop
        }

        public static RoundDef PickFor(CoatProfile who, int seed) =>
            Pick(who != null ? who.RecentRounds : null, seed);

        /// Draw, remember it, and make it current. The one call a game start
        /// needs.
        ///
        /// Seed defaults to something that differs run to run; pass one
        /// explicitly to reproduce a draw, which is what the tests do and what
        /// a host would do if it ever wanted to replay a sequence.
        public static RoundDef Begin(CoatProfile who, int? seed = null)
        {
            int s = seed ?? System.Environment.TickCount;
            var d = PickFor(who, s);
            if (d != null)
            {
                Record(who, d.Id);
                CoatSave.Save(who);
            }
            Current = d;
            return d;
        }

        /// Push a round onto the history and trim it.
        ///
        /// Recorded at the START of a round rather than the end: a round you
        /// walked away from still counts as one you have just seen, and should
        /// not be the very next thing offered.
        ///
        /// Deliberately does NOT write to disk -- Begin does that. A harness
        /// simulating thousands of rounds would otherwise be thousands of
        /// writes to the player's real profile, which is both slow and rude.
        public static void Record(CoatProfile who, string id)
        {
            if (who == null || string.IsNullOrEmpty(id)) return;
            who.RecentRounds ??= new List<string>();
            who.RecentRounds.Add(id);
            while (who.RecentRounds.Count > HistoryCap) who.RecentRounds.RemoveAt(0);
        }
    }
}
