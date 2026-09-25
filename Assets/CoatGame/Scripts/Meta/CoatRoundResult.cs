using UnityEngine;

namespace Coat
{
    public enum RoundOutcome { InProgress, GotAway, Busted }

    /// Turns what happened during one heist into a result and a payout.
    ///
    /// Before this existed, the van closed a round on two things alone: the
    /// loot delivered, and everyone home. Rumbled was never checked -- you
    /// could peg suspicion at 1.0, get caught outright, and still walk away
    /// with a clean time as long as the loot made it back. This is the piece
    /// that makes getting caught mean something.
    ///
    /// **Read EverRumbled, never Rumbled, for the outcome.** Rumbled
    /// self-clears a few seconds after tripping, by design -- so the observer
    /// can start doubting you again rather than staying latched forever. Gate
    /// the ROUND on that live flag and a bust that happened two seconds before
    /// the van pulled in reads as a clean getaway, because the flag had time to
    /// clear before anyone checked it. EverRumbled has a different lifetime: it
    /// only clears when the observer itself is cleared, at the start of the
    /// next attempt. Same underlying signal, deliberately different lifetimes
    /// for two different questions -- "is it suspicious of me RIGHT NOW" versus
    /// "did this run ever get made." See [[coat-networking-decisions]] for why
    /// the observer's own state is split the same way for the host boundary.
    public static class CoatRoundResult
    {
        public static int BasePayout = 100;
        public static int PerFumblePenalty = 15;

        [Tooltip("Coins lost at PEAK suspicion 1.0, scaled down for anything " +
                 "less. Getting away with it at 0.97 pays less than getting " +
                 "away with it at 0.10 -- both are a clean getaway, but they " +
                 "were not the same run.")]
        public static float PeakSuspicionPenalty = 40f;

        /// One settled round, for the HUD to read back.
        public readonly struct Result
        {
            public readonly RoundOutcome Outcome;
            public readonly int Payout;
            public readonly int Fumbles;
            public readonly float PeakSuspicion;
            public readonly float Seconds;

            public Result(RoundOutcome outcome, int payout, int fumbles,
                          float peakSuspicion, float seconds)
            {
                Outcome = outcome; Payout = payout; Fumbles = fumbles;
                PeakSuspicion = peakSuspicion; Seconds = seconds;
            }
        }

        /// The last round settled this session. Null before the first one.
        public static Result? Current { get; private set; }

        public static void Clear() => Current = null;

        // ---- pure logic ----------------------------------------------------

        /// Busted beats everything else -- a fumble-free, low-suspicion run
        /// that still got made at the last second is a bust, full stop. There
        /// is no partial credit for having been smooth about it right up until
        /// you weren't.
        public static RoundOutcome Resolve(bool everRumbled) =>
            everRumbled ? RoundOutcome.Busted : RoundOutcome.GotAway;

        /// Busted pays nothing. Not a reduced amount -- exactly zero. A heist
        /// game where getting caught still pays out has no stakes, and stakes
        /// are the entire engine of the joke: four people only panic about the
        /// walk if something is actually on the line.
        public static int Payout(RoundOutcome outcome, int fumbles, float peakSuspicion)
        {
            if (outcome != RoundOutcome.GotAway) return 0;

            int pay = BasePayout;
            pay -= fumbles * PerFumblePenalty;
            pay -= Mathf.RoundToInt(Mathf.Clamp01(peakSuspicion) * PeakSuspicionPenalty);
            return Mathf.Max(0, pay);
        }

        /// Resolve and price a run, nothing more. No CoatProfile touched, no
        /// disk touched -- safe to call from a test on throwaway numbers.
        public static Result Evaluate(bool everRumbled, int fumbles, float peakSuspicion,
                                       float seconds)
        {
            var outcome = Resolve(everRumbled);
            int pay = Payout(outcome, fumbles, peakSuspicion);
            return new Result(outcome, pay, fumbles, peakSuspicion, seconds);
        }

        // ---- the one call a round close needs -------------------------------

        /// Evaluate, pay out into the profile, save, and remember it for the
        /// HUD. Called exactly once, at the moment a round actually closes.
        ///
        /// NOT for a test to call with a scratch profile: CoatSave.Save writes
        /// to the same fixed file on disk no matter which CoatProfile object
        /// you hand it, so calling this with anything but the player's own
        /// profile overwrites their real save. A test wants Evaluate instead --
        /// same math, touches nothing.
        public static Result Settle(bool everRumbled, int fumbles, float peakSuspicion,
                                     float seconds, CoatProfile who = null)
        {
            var result = Evaluate(everRumbled, fumbles, peakSuspicion, seconds);

            who ??= CoatSave.Current;
            if (result.Payout > 0)
            {
                who.Coins += result.Payout;
                CoatSave.Save(who);
            }

            Current = result;
            return result;
        }
    }
}
