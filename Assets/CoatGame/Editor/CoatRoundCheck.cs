using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Coat;

/// Does the round draw actually behave?
///
/// Two things have to be true at once, and they pull against each other:
///
///   - what you just played should rarely come straight back;
///   - over a long session every round should still show up about equally.
///
/// A naive "never repeat" gives the first and quietly breaks the second, so
/// both are measured here over thousands of simulated rounds rather than
/// eyeballed over five.
///
/// No physics, no play mode, and no writes to the real profile.
public static class CoatRoundCheck
{
    static int _pass, _fail;
    static StringBuilder _log;

    [MenuItem("Coat/Check Round Draw")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        _pass = _fail = 0;
        _log = new StringBuilder();

        var realStock = CoatRounds.Stock;
        int memoryWas = CoatRounds.Memory;
        float floorWas = CoatRounds.Floor;

        try
        {
            CoatRounds.UseForTesting(Fake(6));

            Curve();
            FreshDrawIsEven();
            WeightsAreRespected();
            RepeatsAreRare();
            LongRunStaysFair();
            Deterministic();
            Awkward();
            Reel();
        }
        finally
        {
            CoatRounds.UseForTesting(realStock);
            CoatRounds.Memory = memoryWas;
            CoatRounds.Floor = floorWas;
            CoatRounds.Clear();
        }

        _log.AppendLine();
        _log.AppendLine(_fail == 0 ? $"ALL {_pass} CHECKS PASSED"
                                   : $"{_pass} passed, {_fail} FAILED");
        return _log.ToString();
    }

    // ---- fixtures ----------------------------------------------------

    static CoatRoundStock Fake(int n)
    {
        var s = ScriptableObject.CreateInstance<CoatRoundStock>();
        for (int i = 1; i <= n; i++)
            s.Rounds.Add(new RoundDef { Id = "r" + i, Name = "Round " + i, Weight = 1f });
        return s;
    }

    static void Ok(string what, bool good, string detail)
    {
        if (good) _pass++; else _fail++;
        _log.AppendLine($"  {(good ? "ok  " : "FAIL")} {what,-46} {detail}");
    }

    static void Near(string what, float got, float want, float tol)
        => Ok(what, Mathf.Abs(got - want) <= tol,
              $"{got:0.00} (want {want:0.00} +-{tol:0.00})");

    // ---- checks ------------------------------------------------------

    /// The penalty curve on its own, so the shape is visible and not inferred.
    static void Curve()
    {
        CoatRounds.Memory = 5;
        CoatRounds.Floor = 0.05f;

        _log.AppendLine("repeat penalty by how many rounds ago it was played:");
        var parts = new List<string>();
        for (int ago = 0; ago <= 6; ago++)
            parts.Add($"{ago}:{CoatRounds.RepeatWeight(ago):0.00}");
        _log.AppendLine("    never:1.00  " + string.Join("  ", parts));

        Ok("just played is at the floor",
           Mathf.Approximately(CoatRounds.RepeatWeight(0), 0.05f),
           CoatRounds.RepeatWeight(0).ToString("0.00"));
        Ok("fully recovered after Memory rounds",
           Mathf.Approximately(CoatRounds.RepeatWeight(5), 1f),
           CoatRounds.RepeatWeight(5).ToString("0.00"));
        Ok("never played is full weight",
           Mathf.Approximately(CoatRounds.RepeatWeight(-1), 1f),
           CoatRounds.RepeatWeight(-1).ToString("0.00"));
        Ok("penalty only ever climbs",
           CoatRounds.RepeatWeight(0) < CoatRounds.RepeatWeight(1) &&
           CoatRounds.RepeatWeight(1) < CoatRounds.RepeatWeight(2) &&
           CoatRounds.RepeatWeight(2) < CoatRounds.RepeatWeight(3), "monotonic");
        _log.AppendLine();
    }

    /// With nothing played, all six should be equally likely.
    static void FreshDrawIsEven()
    {
        const int n = 12000;
        var hits = new Dictionary<string, int>();
        var empty = new List<string>();

        for (int i = 0; i < n; i++)
        {
            var d = CoatRounds.Pick(empty, i);
            hits[d.Id] = hits.TryGetValue(d.Id, out int c) ? c + 1 : 1;
        }

        _log.AppendLine($"a first-ever draw, {n} times (6 rounds, so 16.7% each):");
        float worst = 0f;
        foreach (var r in CoatRounds.All)
        {
            float pct = 100f * hits.GetValueOrDefault(r.Id) / n;
            worst = Mathf.Max(worst, Mathf.Abs(pct - 16.667f));
            _log.AppendLine($"    {r.Id}  {pct,5:0.0}%");
        }
        Ok("every round within 2 points of even", worst <= 2f, $"worst off by {worst:0.0}");
        _log.AppendLine();
    }

    /// Designer weights should come out in proportion.
    ///
    /// Worth its own check because the even-draw test above cannot catch a
    /// biased roll: six equal buckets are filled evenly by ANY sweep across
    /// the range, including a badly correlated one. Uneven buckets are not.
    static void WeightsAreRespected()
    {
        var stock = ScriptableObject.CreateInstance<CoatRoundStock>();
        stock.Rounds.Add(new RoundDef { Id = "w1", Name = "One",   Weight = 1f });
        stock.Rounds.Add(new RoundDef { Id = "w2", Name = "Two",   Weight = 2f });
        stock.Rounds.Add(new RoundDef { Id = "w3", Name = "Three", Weight = 3f });
        CoatRounds.UseForTesting(stock);

        const int n = 12000;
        var hits = new Dictionary<string, int>();
        var empty = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var d = CoatRounds.Pick(empty, i * 2654435761u.GetHashCode() + i);
            hits[d.Id] = hits.TryGetValue(d.Id, out int c) ? c + 1 : 1;
        }

        _log.AppendLine($"weights 1:2:3 over {n} draws (so 16.7 / 33.3 / 50.0%):");
        foreach (var id in new[] { "w1", "w2", "w3" })
            _log.AppendLine($"    {id}  {100f * hits.GetValueOrDefault(id) / n,5:0.0}%");

        Near("w1 near 16.7%", 100f * hits.GetValueOrDefault("w1") / n, 16.7f, 2f);
        Near("w2 near 33.3%", 100f * hits.GetValueOrDefault("w2") / n, 33.3f, 2f);
        Near("w3 near 50.0%", 100f * hits.GetValueOrDefault("w3") / n, 50.0f, 2f);

        CoatRounds.UseForTesting(Fake(6));
        _log.AppendLine();
    }

    /// Play 8000 rounds back to back and count how often one repeats.
    static void RepeatsAreRare()
    {
        const int n = 8000;
        var p = new CoatProfile();
        string last = null;
        int backToBack = 0, withinThree = 0;
        var recentWindow = new List<string>();

        for (int i = 0; i < n; i++)
        {
            var d = CoatRounds.PickFor(p, i * 7919 + 13);
            if (d.Id == last) backToBack++;
            if (recentWindow.Contains(d.Id)) withinThree++;

            recentWindow.Add(d.Id);
            if (recentWindow.Count > 3) recentWindow.RemoveAt(0);

            last = d.Id;
            CoatRounds.Record(p, d.Id);
        }

        float repeatPct = 100f * backToBack / n;
        float threePct = 100f * withinThree / n;

        _log.AppendLine($"{n} rounds played in a row:");
        _log.AppendLine($"    immediate repeat   {repeatPct,5:0.0}%   (evenly it would be 16.7%)");
        _log.AppendLine($"    seen in last three {threePct,5:0.0}%   (evenly it would be 50.0%)");

        Ok("immediate repeats under 4%", repeatPct < 4f, $"{repeatPct:0.0}%");
        Ok("still possible, not banned outright", backToBack > 0, $"{backToBack} of {n}");
        Ok("repeats within three well under even", threePct < 30f, $"{threePct:0.0}%");
        _log.AppendLine();
    }

    /// The bit a "never repeat" rule gets wrong: over a long session, is the
    /// spread still fair?
    static void LongRunStaysFair()
    {
        const int n = 12000;
        var p = new CoatProfile();
        var hits = new Dictionary<string, int>();

        for (int i = 0; i < n; i++)
        {
            var d = CoatRounds.PickFor(p, i * 104729 + 7);
            hits[d.Id] = hits.TryGetValue(d.Id, out int c) ? c + 1 : 1;
            CoatRounds.Record(p, d.Id);
        }

        _log.AppendLine($"spread across a {n} round session:");
        float worst = 0f;
        foreach (var r in CoatRounds.All)
        {
            float pct = 100f * hits.GetValueOrDefault(r.Id) / n;
            worst = Mathf.Max(worst, Mathf.Abs(pct - 16.667f));
            _log.AppendLine($"    {r.Id}  {pct,5:0.0}%");
        }
        Ok("rarity did not bias the long run", worst <= 2f, $"worst off by {worst:0.0}");
        Ok("history stayed capped", p.RecentRounds.Count <= CoatRounds.HistoryCap,
           $"{p.RecentRounds.Count} <= {CoatRounds.HistoryCap}");
        _log.AppendLine();
    }

    /// Same history, same seed, same answer -- what a host would rely on.
    static void Deterministic()
    {
        var p = new CoatProfile();
        CoatRounds.Record(p, "r3");
        CoatRounds.Record(p, "r1");

        string first = CoatRounds.PickFor(p, 4242).Id;
        bool same = true;
        for (int i = 0; i < 200; i++)
            if (CoatRounds.PickFor(p, 4242).Id != first) { same = false; break; }

        Ok("same seed and history repeats exactly", same, first);

        int differ = 0;
        for (int s = 0; s < 200; s++)
            if (CoatRounds.PickFor(p, s).Id != first) differ++;
        Ok("different seeds do move it", differ > 0, $"{differ} of 200 differed");
        _log.AppendLine();
    }

    /// The reveal's one job: land on the round that was drawn.
    ///
    /// Stepped at a real frame rate rather than one giant tick, because an
    /// off-by-one in the landing would only show up as the easing crawls into
    /// its last few pixels -- exactly the part a single big step skips.
    static void Reel()
    {
        CoatRounds.UseForTesting(Fake(6));
        const float dt = 1f / 60f;

        _log.AppendLine("the reveal reel:");

        int landedRight = 0, runs = 300, movedMidway = 0;
        for (int n = 0; n < runs; n++)
        {
            var winner = CoatRounds.All[n % CoatRounds.All.Count];
            var reel = new CoatRoundReel();
            reel.Show(winner, null);

            bool sawOther = false;
            int guard = 0;
            while (!reel.Landed && guard++ < 2000)
            {
                reel.Tick(dt);
                if (!reel.Landed && reel.Centred != winner) sawOther = true;
            }
            if (reel.Landed && reel.Centred == winner) landedRight++;
            if (sawOther) movedMidway++;
        }
        Ok("lands on the drawn round, every time", landedRight == runs,
           $"{landedRight} of {runs}");
        Ok("and actually passes other cards first", movedMidway > runs * 0.9f,
           $"{movedMidway} of {runs}");

        // Finishing: the callback fires once, after the hold, not before.
        {
            var reel = new CoatRoundReel();
            int fired = 0;
            reel.Show(CoatRounds.All[2], () => fired++);
            float t = 0f;
            bool firedEarly = false;
            while (reel.Running && t < 20f)
            {
                reel.Tick(dt);
                t += dt;
                if (fired > 0 && t < reel.SpinSeconds + reel.HoldSeconds - 0.05f) firedEarly = true;
            }
            for (int i = 0; i < 60; i++) reel.Tick(dt);      // keep ticking past the end
            Ok("done fires exactly once", fired == 1, $"fired {fired}");
            Ok("and not before spin + hold", !firedEarly,
               $"finished at {t:0.00}s (spin {reel.SpinSeconds} + hold {reel.HoldSeconds})");
        }

        // Skip mid-spin jumps to the landing, still on the winner.
        {
            var reel = new CoatRoundReel();
            var w = CoatRounds.All[4];
            reel.Show(w, null);
            for (int i = 0; i < 20; i++) reel.Tick(dt);
            reel.Skip();
            reel.Tick(dt);
            Ok("skip lands immediately, on the winner", reel.Landed && reel.Centred == w,
               reel.Centred == null ? "null" : reel.Centred.Id);

            int fired = 0;
            var r2 = new CoatRoundReel();
            r2.Show(w, () => fired++);
            r2.Skip(); r2.Tick(dt);                      // to the landing
            r2.Skip();                                   // and past the hold
            Ok("a second skip finishes it", !r2.Running && fired == 1, $"fired {fired}");
        }

        // Nothing to show.
        {
            var reel = new CoatRoundReel();
            int fired = 0;
            reel.Show(null, () => fired++);
            Ok("no round: finishes at once, never shows", !reel.Running && fired == 1,
               $"running {reel.Running}, fired {fired}");
        }
        _log.AppendLine();
    }

    /// Small and broken pools.
    static void Awkward()
    {
        var p = new CoatProfile();

        // One round only. The floor exists so this cannot deadlock.
        CoatRounds.UseForTesting(Fake(1));
        bool always = true;
        for (int i = 0; i < 100; i++)
        {
            var d = CoatRounds.PickFor(p, i);
            if (d == null || d.Id != "r1") { always = false; break; }
            CoatRounds.Record(p, d.Id);
        }
        Ok("a one round pool keeps drawing it", always, "100 draws, never null");

        // Two rounds. Should alternate hard without locking.
        CoatRounds.UseForTesting(Fake(2));
        var q = new CoatProfile();
        int swaps = 0; string prev = null;
        for (int i = 0; i < 200; i++)
        {
            var d = CoatRounds.PickFor(q, i * 31 + 5);
            if (prev != null && d.Id != prev) swaps++;
            prev = d.Id;
            CoatRounds.Record(q, d.Id);
        }
        Ok("a two round pool alternates", swaps > 150, $"{swaps} swaps in 199");

        // Nothing at all.
        CoatRounds.UseForTesting(ScriptableObject.CreateInstance<CoatRoundStock>());
        Ok("an empty pool returns null and does not throw",
           CoatRounds.PickFor(p, 1) == null, "null");

        // Everything parked at weight 0.
        var parked = Fake(3);
        foreach (var r in parked.Rounds) r.Weight = 0f;
        CoatRounds.UseForTesting(parked);
        var got = CoatRounds.PickFor(p, 9);
        Ok("all rounds parked still starts a game", got != null, got == null ? "null" : got.Id);

        // One parked among live ones must never come up.
        var mixed = Fake(3);
        mixed.Rounds[1].Weight = 0f;
        CoatRounds.UseForTesting(mixed);
        bool drewParked = false;
        for (int i = 0; i < 500; i++)
            if (CoatRounds.Pick(new List<string>(), i).Id == "r2") { drewParked = true; break; }
        Ok("a parked round is never drawn", !drewParked, "500 draws");

        // RoundsAgo, since every weight leans on it.
        var hist = new List<string> { "r1", "r2", "r3" };   // oldest .. newest
        Ok("RoundsAgo newest is 0", CoatRounds.RoundsAgo(hist, "r3") == 0, "r3");
        Ok("RoundsAgo oldest is 2", CoatRounds.RoundsAgo(hist, "r1") == 2, "r1");
        Ok("RoundsAgo unseen is -1", CoatRounds.RoundsAgo(hist, "r9") == -1, "r9");
        _log.AppendLine();
    }
}
