using System.Text;
using UnityEditor;
using UnityEngine;
using Coat;

/// Does a settled round pay out the way it is supposed to?
///
/// Everything here goes through Evaluate(), never Settle() -- Settle writes to
/// the player's real save file no matter which CoatProfile you hand it (same
/// fixed path every time), so a test calling it on scratch numbers would
/// quietly overwrite the actual save. Evaluate does the identical math with
/// nothing touched. No physics, no play mode.
public static class CoatRoundResultCheck
{
    static int _pass, _fail;
    static StringBuilder _log;

    [MenuItem("Coat/Check Round Result")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        _pass = _fail = 0;
        _log = new StringBuilder();

        int baseWas = CoatRoundResult.BasePayout;
        int fumbleWas = CoatRoundResult.PerFumblePenalty;
        float suspWas = CoatRoundResult.PeakSuspicionPenalty;

        try
        {
            CoatRoundResult.BasePayout = 100;
            CoatRoundResult.PerFumblePenalty = 15;
            CoatRoundResult.PeakSuspicionPenalty = 40f;

            Resolving();
            CleanPayout();
            FumblesCost();
            SuspicionCosts();
            NeverNegative();
            BustedIsAlwaysZero();
            EvaluateShape();
        }
        finally
        {
            CoatRoundResult.BasePayout = baseWas;
            CoatRoundResult.PerFumblePenalty = fumbleWas;
            CoatRoundResult.PeakSuspicionPenalty = suspWas;
            CoatRoundResult.Clear();
        }

        _log.AppendLine();
        _log.AppendLine(_fail == 0 ? $"ALL {_pass} CHECKS PASSED" : $"{_pass} passed, {_fail} FAILED");
        return _log.ToString();
    }

    static void Is(string what, object got, object want)
    {
        bool ok = Equals(got, want);
        if (ok) _pass++; else _fail++;
        _log.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what,-46} {got}" + (ok ? "" : $"   wanted {want}"));
    }

    // ---- checks ------------------------------------------------------

    /// The one rule that matters most: getting made even briefly outweighs
    /// everything else about how the run went.
    static void Resolving()
    {
        _log.AppendLine("resolving an outcome from EverRumbled:");
        Is("never rumbled -> GotAway", CoatRoundResult.Resolve(false), RoundOutcome.GotAway);
        Is("ever rumbled -> Busted",   CoatRoundResult.Resolve(true),  RoundOutcome.Busted);
        _log.AppendLine();
    }

    static void CleanPayout()
    {
        _log.AppendLine("a perfectly clean getaway (0 fumbles, 0 peak suspicion):");
        int pay = CoatRoundResult.Payout(RoundOutcome.GotAway, 0, 0f);
        Is("pays exactly BasePayout", pay, CoatRoundResult.BasePayout);
        _log.AppendLine();
    }

    static void FumblesCost()
    {
        _log.AppendLine("fumbles, at 0 peak suspicion:");
        Is("1 fumble",  CoatRoundResult.Payout(RoundOutcome.GotAway, 1, 0f), 100 - 15);
        Is("2 fumbles", CoatRoundResult.Payout(RoundOutcome.GotAway, 2, 0f), 100 - 30);
        Is("4 fumbles", CoatRoundResult.Payout(RoundOutcome.GotAway, 4, 0f), 100 - 60);
        _log.AppendLine();
    }

    static void SuspicionCosts()
    {
        _log.AppendLine("peak suspicion, at 0 fumbles:");
        Is("peak 1.00 (a photo finish)", CoatRoundResult.Payout(RoundOutcome.GotAway, 0, 1f), 100 - 40);
        Is("peak 0.50",                  CoatRoundResult.Payout(RoundOutcome.GotAway, 0, 0.5f), 100 - 20);
        Is("peak 0.00 (never seen)",     CoatRoundResult.Payout(RoundOutcome.GotAway, 0, 0f), 100);

        // Defensive: out-of-range input should not pay MORE than a clean run,
        // and should not go negative on its own.
        Is("peak clamps above 1", CoatRoundResult.Payout(RoundOutcome.GotAway, 0, 1.4f), 100 - 40);
        Is("peak clamps below 0", CoatRoundResult.Payout(RoundOutcome.GotAway, 0, -0.3f), 100);
        _log.AppendLine();
    }

    /// A shambolic run (many fumbles, high suspicion) should never come out
    /// owing coins.
    static void NeverNegative()
    {
        _log.AppendLine("a genuinely bad but ungotten-away-with run:");
        int pay = CoatRoundResult.Payout(RoundOutcome.GotAway, 12, 1f);
        Is("floors at zero, not negative", pay, 0);
        _log.AppendLine();
    }

    /// The one that made this worth building: getting caught pays nothing,
    /// full stop, regardless of how clean the rest of the run looked.
    static void BustedIsAlwaysZero()
    {
        _log.AppendLine("busted, at every combination of fumbles/suspicion:");
        Is("busted, 0 fumbles, 0 suspicion",   CoatRoundResult.Payout(RoundOutcome.Busted, 0, 0f), 0);
        Is("busted, 0 fumbles, LOW suspicion", CoatRoundResult.Payout(RoundOutcome.Busted, 0, 0.1f), 0);
        Is("busted despite a flawless run otherwise",
           CoatRoundResult.Payout(RoundOutcome.Busted, 0, 0f), 0);
        Is("busted, several fumbles too",      CoatRoundResult.Payout(RoundOutcome.Busted, 5, 0.8f), 0);
        _log.AppendLine();
    }

    /// Evaluate is the thing production code and a test both call -- check it
    /// actually agrees with Resolve/Payout rather than duplicating their logic
    /// and drifting from it.
    static void EvaluateShape()
    {
        _log.AppendLine("Evaluate ties Resolve + Payout together correctly:");

        var clean = CoatRoundResult.Evaluate(everRumbled: false, fumbles: 1, peakSuspicion: 0.2f, seconds: 42f);
        Is("outcome", clean.Outcome, RoundOutcome.GotAway);
        Is("payout matches Payout() directly", clean.Payout,
           CoatRoundResult.Payout(RoundOutcome.GotAway, 1, 0.2f));
        Is("fumbles preserved for display", clean.Fumbles, 1);
        Is("peak suspicion preserved for display", Mathf.Approximately(clean.PeakSuspicion, 0.2f), true);
        Is("seconds preserved for display", Mathf.Approximately(clean.Seconds, 42f), true);

        var bust = CoatRoundResult.Evaluate(everRumbled: true, fumbles: 0, peakSuspicion: 0f, seconds: 11f);
        Is("busted outcome", bust.Outcome, RoundOutcome.Busted);
        Is("busted payout", bust.Payout, 0);

        // Evaluate must not touch Current or any CoatProfile -- only Settle does.
        CoatRoundResult.Clear();
        CoatRoundResult.Evaluate(false, 0, 0f, 1f);
        Is("Evaluate never sets Current", CoatRoundResult.Current.HasValue, false);
        _log.AppendLine();
    }
}
