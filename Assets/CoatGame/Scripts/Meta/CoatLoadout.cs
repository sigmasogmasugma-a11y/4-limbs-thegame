using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Who ends up wearing what, once roles are dealt.
    ///
    /// The shop sells by PAIR -- Legs, Arms -- but a match hands each player
    /// one specific limb. The rule:
    ///
    ///     bought Legs, drew RightLeg  ->  it goes on the right leg
    ///     bought Legs, drew LeftLeg   ->  the same purchase, on the left leg
    ///     bought Legs, drew an arm    ->  nothing
    ///
    /// That last line is the one worth being careful about, because it fails
    /// in TWO directions at once and both have to hold:
    ///
    ///   - the arm they actually drew stays bare. Their leg cosmetic does not
    ///     migrate onto it.
    ///   - the legs get nothing out of it either. It does not transfer to
    ///     whoever DID draw a leg, and it does not sit on the body unworn.
    ///
    /// So a limb is dressed only by the player standing in it, and only out of
    /// the category that matches that limb. Nothing migrates, nothing pools.
    /// Arms work the same way in reverse.
    public static class CoatLoadout
    {
        /// The only slot this role can wear. The other pair's slot is not
        /// consulted at all -- that is what makes the mismatch come out bare
        /// rather than falling back to whatever else they own.
        public static CosmeticSlot SlotFor(CoatRole role) =>
            role == CoatRole.LeftLeg || role == CoatRole.RightLeg
                ? CosmeticSlot.Legs
                : CosmeticSlot.Arms;

        /// What this player's limb wears. Null is a bare limb and is normal.
        public static string LimbWear(CoatProfile who, CoatRole role) =>
            who == null ? null : who.EquippedIn(SlotFor(role));

        /// One limb's result.
        public struct Worn
        {
            public CoatRole Role;
            public CosmeticSlot Slot;
            public string Id;
            public bool Any => !string.IsNullOrEmpty(Id);
        }

        public class Dressing
        {
            /// Indexed by CoatRole.
            public Worn[] Limbs = new Worn[4];
            public string Cloak;
            public string Head;

            public Worn Limb(CoatRole r) => Limbs[(int)r];
            public int LimbsDressed
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Limbs.Length; i++) if (Limbs[i].Any) n++;
                    return n;
                }
            }
        }

        /// Whose pick breaks a tie on the shared slots.
        ///
        /// The person who opened the lobby. Offline, with all four on one
        /// keyboard, that is player one. Once Fusion is in it is the host's
        /// seat, set when the session starts.
        public static CoatRole Leader = CoatRole.LeftLeg;

        /// There is ONE coat and ONE head between the four of them, so a shared
        /// slot cannot simply belong to everyone who equipped one. It is a
        /// vote: whichever is picked MOST goes on the body, and if the top is
        /// tied the leader's pick wins it.
        ///
        /// Having nothing equipped is not a vote for going without -- somebody
        /// who owns no coat just has no say in which coat. So one person with a
        /// coat and three with none is a majority of one, and the body wears it.
        ///
        /// Every step is a pure function of the equipped ids and the leader's
        /// seat, so each peer arrives at the same coat with no message about
        /// it -- which is what keeps this off the wire once Fusion lands.
        public static string SharedWear(CoatProfile[] bySeat, CosmeticSlot slot)
            => SharedWear(bySeat, slot, Leader);

        public static string SharedWear(CoatProfile[] bySeat, CosmeticSlot slot, CoatRole leader)
        {
            if (bySeat == null) return null;

            var ids = new List<string>();
            var votes = new List<int>();
            var firstSeat = new List<int>();

            for (int seat = 0; seat < bySeat.Length; seat++)
            {
                string id = bySeat[seat] == null ? null : bySeat[seat].EquippedIn(slot);
                if (string.IsNullOrEmpty(id)) continue;

                int k = ids.IndexOf(id);
                if (k < 0) { ids.Add(id); votes.Add(1); firstSeat.Add(seat); }
                else votes[k]++;
            }

            if (ids.Count == 0) return null;

            int best = 0;
            for (int i = 1; i < votes.Count; i++) if (votes[i] > votes[best]) best = i;

            int tied = 0;
            for (int i = 0; i < votes.Count; i++) if (votes[i] == votes[best]) tied++;
            if (tied == 1) return ids[best];

            // Tied at the top. The leader's own pick takes it, provided they
            // voted -- their vote is already in the count, so it can only be
            // one of the tied ones.
            int ls = (int)leader;
            string leaderPick = ls >= 0 && ls < bySeat.Length && bySeat[ls] != null
                              ? bySeat[ls].EquippedIn(slot) : null;
            if (!string.IsNullOrEmpty(leaderPick))
            {
                int k = ids.IndexOf(leaderPick);
                if (k >= 0 && votes[k] == votes[best]) return leaderPick;
            }

            // Leader had none of their own. Lowest seat among the tied, so the
            // answer is still the same on every machine.
            int pick = -1;
            for (int i = 0; i < ids.Count; i++)
                if (votes[i] == votes[best] && (pick < 0 || firstSeat[i] < firstSeat[pick]))
                    pick = i;
            return ids[pick];
        }

        /// bySeat is indexed by CoatRole: bySeat[(int)CoatRole.RightLeg] is the
        /// profile of whoever drew the right leg. A null seat is an empty slot
        /// and comes out bare.
        public static Dressing Resolve(CoatProfile[] bySeat)
        {
            var d = new Dressing();
            for (int i = 0; i < d.Limbs.Length; i++)
            {
                var role = (CoatRole)i;
                var slot = SlotFor(role);
                var who = bySeat != null && i < bySeat.Length ? bySeat[i] : null;
                d.Limbs[i] = new Worn { Role = role, Slot = slot, Id = LimbWear(who, role) };
            }
            d.Cloak = SharedWear(bySeat, CosmeticSlot.Cloak);
            d.Head  = SharedWear(bySeat, CosmeticSlot.Head);
            return d;
        }
    }

    /// Where a worn cosmetic hangs off the rig.
    ///
    /// Bone names, matching what CoatRagdollBuilder names the rigid bodies and
    /// what CoatSkin.SubmeshOwner already keys off. Same names on the disguise
    /// and on a crew child, so one lookup serves both.
    public static class CoatFit
    {
        public static string BoneFor(CoatRole role)
        {
            switch (role)
            {
                case CoatRole.LeftLeg:  return "ThighL";
                case CoatRole.RightLeg: return "ThighR";
                case CoatRole.LeftArm:  return "UpperArmL";
                default:                return "UpperArmR";
            }
        }

        public static string BoneFor(CosmeticSlot shared) =>
            shared == CosmeticSlot.Head ? "Head" : "Torso";

        /// The limb's colour, for menus and readouts. CoatPalette is the one
        /// table: the meshes (CoatDressUp) read the same values.
        public static Color Colour(CoatRole role) => CoatPalette.Limb(role);

        public static string Label(CoatRole role)
        {
            switch (role)
            {
                case CoatRole.LeftLeg:  return "Left Leg";
                case CoatRole.RightLeg: return "Right Leg";
                case CoatRole.LeftArm:  return "Left Arm";
                default:                return "Right Arm";
            }
        }
    }
}
