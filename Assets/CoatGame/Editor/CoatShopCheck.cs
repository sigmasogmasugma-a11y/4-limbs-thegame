using System.Text;
using UnityEditor;
using UnityEngine;
using Coat;

/// Does a purchase land on the limb the player actually drew?
///
/// The rule reads simply and fails in two directions at once, so both get
/// checked here rather than eyeballed:
///
///   bought Legs, drew RightLeg -> on the right leg
///   bought Legs, drew LeftLeg  -> same purchase, on the left leg
///   bought Legs, drew an arm   -> that arm is bare, AND the legs get nothing
///                                 out of it either
///
/// No physics and no play mode: this is all CoatLoadout, which is why it can
/// be trusted in a way the stepping harness currently cannot.
public static class CoatShopCheck
{
    static int _pass, _fail;
    static StringBuilder _log;

    [MenuItem("Coat/Check Shop Rules")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        _pass = _fail = 0;
        _log = new StringBuilder();

        var realStock = CoatCatalogue.Stock;
        try
        {
            CoatCatalogue.UseForTesting(FakeStock());

            LimbMatrix();
            TheMismatch();
            WholeBody();
            Shared();
            Economy();
            RoundTrip();
        }
        finally
        {
            CoatCatalogue.UseForTesting(realStock);
        }

        _log.AppendLine();
        _log.AppendLine(_fail == 0
            ? $"ALL {_pass} CHECKS PASSED"
            : $"{_pass} passed, {_fail} FAILED");
        return _log.ToString();
    }

    // ---- fixtures ----------------------------------------------------

    static CoatShopStock FakeStock()
    {
        var s = ScriptableObject.CreateInstance<CoatShopStock>();
        s.Items.Add(new CosmeticDef { Id = "boots",  Name = "Boots",  Slot = CosmeticSlot.Legs,  Price = 100 });
        s.Items.Add(new CosmeticDef { Id = "socks",  Name = "Socks",  Slot = CosmeticSlot.Legs,  Price = 40  });
        s.Items.Add(new CosmeticDef { Id = "gloves", Name = "Gloves", Slot = CosmeticSlot.Arms,  Price = 100 });
        s.Items.Add(new CosmeticDef { Id = "mac",    Name = "Mac",    Slot = CosmeticSlot.Cloak, Price = 300 });
        s.Items.Add(new CosmeticDef { Id = "cape",   Name = "Cape",   Slot = CosmeticSlot.Cloak, Price = 300 });
        s.Items.Add(new CosmeticDef { Id = "poncho", Name = "Poncho", Slot = CosmeticSlot.Cloak, Price = 300 });
        s.Items.Add(new CosmeticDef { Id = "hat",    Name = "Hat",    Slot = CosmeticSlot.Head,  Price = 250 });
        s.Items.Add(new CosmeticDef { Id = "fez",    Name = "Fez",    Slot = CosmeticSlot.Head,  Price = 250 });
        return s;
    }

    /// A player who owns and wears everything passed in.
    static CoatProfile Wearing(params string[] ids)
    {
        var p = new CoatProfile { Coins = 9999 };
        foreach (var id in ids)
        {
            var d = CoatCatalogue.Find(id);
            p.Owned.Add(id);
            p.Equipped[(int)d.Slot] = id;
        }
        return p;
    }

    static void Is(string what, string got, string want)
    {
        bool ok = got == want;
        if (ok) _pass++; else _fail++;
        string g = string.IsNullOrEmpty(got) ? "(bare)" : got;
        string w = string.IsNullOrEmpty(want) ? "(bare)" : want;
        _log.AppendLine($"  {(ok ? "ok  " : "FAIL")} {what,-44} {g,-10} {(ok ? "" : "wanted " + w)}");
    }

    static void Is(string what, bool got, bool want)
        => Is(what, got.ToString(), want.ToString());

    static void Is(string what, int got, int want)
        => Is(what, got.ToString(), want.ToString());

    // ---- checks ------------------------------------------------------

    /// Bought both pairs: whichever limb they draw, they are dressed.
    static void LimbMatrix()
    {
        _log.AppendLine("bought BOTH boots and gloves, drawn into each role:");
        var p = Wearing("boots", "gloves");

        for (int i = 0; i < 4; i++)
        {
            var role = (CoatRole)i;
            string want = CoatLoadout.SlotFor(role) == CosmeticSlot.Legs ? "boots" : "gloves";
            Is($"{CoatFit.Label(role)} wears -> {CoatFit.BoneFor(role)}",
               CoatLoadout.LimbWear(p, role), want);
        }
        _log.AppendLine();
    }

    /// The one the request was really about.
    static void TheMismatch()
    {
        _log.AppendLine("bought ONLY boots (legs), drawn into each role:");
        var p = Wearing("boots");

        Is("Left Leg  wears",  CoatLoadout.LimbWear(p, CoatRole.LeftLeg),  "boots");
        Is("Right Leg wears",  CoatLoadout.LimbWear(p, CoatRole.RightLeg), "boots");
        Is("Left Arm  wears",  CoatLoadout.LimbWear(p, CoatRole.LeftArm),  null);
        Is("Right Arm wears",  CoatLoadout.LimbWear(p, CoatRole.RightArm), null);
        _log.AppendLine();

        _log.AppendLine("bought ONLY gloves (arms), drawn into each role:");
        var q = Wearing("gloves");
        Is("Left Arm  wears",  CoatLoadout.LimbWear(q, CoatRole.LeftArm),  "gloves");
        Is("Right Arm wears",  CoatLoadout.LimbWear(q, CoatRole.RightArm), "gloves");
        Is("Left Leg  wears",  CoatLoadout.LimbWear(q, CoatRole.LeftLeg),  null);
        Is("Right Leg wears",  CoatLoadout.LimbWear(q, CoatRole.RightLeg), null);
        _log.AppendLine();
    }

    /// Four real seats, to prove nothing migrates BETWEEN players either.
    static void WholeBody()
    {
        _log.AppendLine("four seats: the boots buyer draws RIGHT ARM, everyone else is broke");

        var bootsBuyer = Wearing("boots");
        var broke = new CoatProfile();

        var bySeat = new CoatProfile[4];
        bySeat[(int)CoatRole.LeftLeg]  = broke;
        bySeat[(int)CoatRole.RightLeg] = broke;
        bySeat[(int)CoatRole.LeftArm]  = broke;
        bySeat[(int)CoatRole.RightArm] = bootsBuyer;

        var d = CoatLoadout.Resolve(bySeat);

        Is("right arm (the buyer) wears", d.Limb(CoatRole.RightArm).Id, null);
        Is("left leg wears",              d.Limb(CoatRole.LeftLeg).Id,  null);
        Is("right leg wears",             d.Limb(CoatRole.RightLeg).Id, null);
        Is("limbs dressed on the body",   d.LimbsDressed, 0);
        _log.AppendLine();

        _log.AppendLine("same buyer, same boots, but this time draws RIGHT LEG");
        bySeat[(int)CoatRole.RightArm] = broke;
        bySeat[(int)CoatRole.RightLeg] = bootsBuyer;
        var e = CoatLoadout.Resolve(bySeat);
        Is("right leg wears",           e.Limb(CoatRole.RightLeg).Id, "boots");
        Is("left leg still wears",      e.Limb(CoatRole.LeftLeg).Id,  null);
        Is("limbs dressed on the body", e.LimbsDressed, 1);
        _log.AppendLine();
    }

    /// Seats in CoatRole order: LeftLeg, RightLeg, LeftArm, RightArm.
    static string Cloak(CoatRole leader, params CoatProfile[] seats)
        => CoatLoadout.SharedWear(seats, CosmeticSlot.Cloak, leader);

    /// One coat, one head, four opinions. Most picked wins; the leader breaks
    /// a tie.
    static void Shared()
    {
        _log.AppendLine("shared slots -- vote, then leader breaks the tie:");

        var mac    = Wearing("mac");
        var cape   = Wearing("cape");
        var poncho = Wearing("poncho");
        var none   = new CoatProfile();

        // The count decides on its own. The leader voted cape and still loses,
        // which is the point of only consulting them on a tie.
        Is("2 mac vs 1 cape, leader wants cape",
           Cloak(CoatRole.RightLeg, mac, cape, mac, none), "mac");

        // Nobody else owns one, so one vote is a majority. Having nothing is
        // not a vote for a bare body.
        Is("one owner, three with nothing",
           Cloak(CoatRole.LeftLeg, none, none, cape, none), "cape");

        // Three different coats, all on one vote. Leader sits at LeftArm.
        Is("1-1-1 tie, leader is the left arm",
           Cloak(CoatRole.LeftArm, mac, cape, poncho, none), "poncho");

        // Two each. Leader is the right arm and voted cape.
        Is("2-2 tie, leader voted cape",
           Cloak(CoatRole.RightArm, mac, mac, cape, cape), "cape");

        // Tied, but the leader owns nothing and so never voted -- falls to the
        // lowest seat among the tied, which is still the same on every peer.
        Is("1-1 tie, leader owns nothing",
           Cloak(CoatRole.RightArm, mac, cape, none, none), "mac");

        Is("nobody owns a coat",
           Cloak(CoatRole.LeftLeg, none, none, none, none), null);

        // Cloak and head are counted separately, off the same four players.
        CoatLoadout.Leader = CoatRole.LeftLeg;
        var bySeat = new CoatProfile[4];
        bySeat[(int)CoatRole.LeftLeg]  = Wearing("mac", "fez");
        bySeat[(int)CoatRole.RightLeg] = Wearing("cape", "hat");
        bySeat[(int)CoatRole.LeftArm]  = Wearing("cape", "hat");
        bySeat[(int)CoatRole.RightArm] = none;

        var d = CoatLoadout.Resolve(bySeat);
        Is("coat: cape has 2 against the leader's 1", d.Cloak, "cape");
        Is("head: hat has 2 against the leader's 1",  d.Head,  "hat");
        _log.AppendLine();
    }

    static void Economy()
    {
        _log.AppendLine("buying and equipping:");
        var p = new CoatProfile { Coins = 120 };
        var boots  = CoatCatalogue.Find("boots");
        var socks  = CoatCatalogue.Find("socks");
        var mac    = CoatCatalogue.Find("mac");

        Is("equip before owning refuses", p.Equip("boots", out _), false);
        Is("mac at 300 on 120 coins refuses", p.Buy(mac, out _), false);
        Is("coins untouched by the refusal", p.Coins, 120);

        Is("buy boots at 100", p.Buy(boots, out _), true);
        Is("coins left", p.Coins, 20);
        Is("buying boots twice refuses", p.Buy(boots, out _), false);

        Is("equip boots", p.Equip("boots", out _), true);
        Is("legs slot holds", p.EquippedIn(CosmeticSlot.Legs), "boots");

        // Same slot: the second one replaces rather than stacks.
        p.Coins = 100;
        p.Buy(socks, out _);
        p.Equip("socks", out _);
        Is("socks replace boots in the legs slot", p.EquippedIn(CosmeticSlot.Legs), "socks");
        Is("boots still owned", p.Owns("boots"), true);

        p.Unequip(CosmeticSlot.Legs);
        Is("legs slot after taking them off", p.EquippedIn(CosmeticSlot.Legs), null);
        _log.AppendLine();
    }

    /// Straight through JsonUtility, not through CoatSave, so running this
    /// cannot overwrite the profile the player actually owns.
    static void RoundTrip()
    {
        _log.AppendLine("save and reload:");
        var p = Wearing("boots", "gloves", "hat");
        p.Coins = 777;
        p.Settings.Volume = 0.33f;
        p.Settings.ShowHud = false;

        var back = JsonUtility.FromJson<CoatProfile>(JsonUtility.ToJson(p));
        back.Repair();

        Is("coins survive",            back.Coins, 777);
        Is("legs equip survives",      back.EquippedIn(CosmeticSlot.Legs), "boots");
        Is("arms equip survives",      back.EquippedIn(CosmeticSlot.Arms), "gloves");
        Is("head equip survives",      back.EquippedIn(CosmeticSlot.Head), "hat");
        Is("cloak still empty",        back.EquippedIn(CosmeticSlot.Cloak), null);
        Is("owned count survives",     back.Owned.Count, 3);
        Is("volume survives",          Mathf.Approximately(back.Settings.Volume, 0.33f), true);
        Is("hud toggle survives",      back.Settings.ShowHud, false);

        // A profile written by an older build with fewer slots must not throw.
        var old = new CoatProfile { Equipped = new string[2] { "boots", null } };
        old.Repair();
        Is("short equip array is grown", old.Equipped.Length, CosmeticSlots.Count);
        Is("and keeps what it had",      old.EquippedIn(CosmeticSlot.Legs), "boots");
        _log.AppendLine();
    }
}
