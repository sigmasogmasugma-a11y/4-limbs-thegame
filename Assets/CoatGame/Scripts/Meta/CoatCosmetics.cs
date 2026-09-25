using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// What a cosmetic is bought FOR.
    ///
    /// A pair-wide category, never one specific limb. You buy "legs", not "the
    /// right leg", because which leg you end up in is not decided until the
    /// match starts and the same purchase has to work on either side.
    /// CoatLoadout does that mapping.
    public enum CosmeticSlot { Legs = 0, Arms = 1, Cloak = 2, Head = 3 }

    public static class CosmeticSlots
    {
        public const int Count = 4;

        public static readonly CosmeticSlot[] All =
        {
            CosmeticSlot.Legs, CosmeticSlot.Arms, CosmeticSlot.Cloak, CosmeticSlot.Head
        };

        /// Worn by ONE player -- whichever of them drew that limb.
        public static bool IsLimb(CosmeticSlot s) =>
            s == CosmeticSlot.Legs || s == CosmeticSlot.Arms;

        /// Part of the disguise as a whole. There is one coat and one head
        /// between the four of them, so unlike a limb these cannot just belong
        /// to whoever equipped one -- see CoatLoadout.SharedPriority.
        public static bool IsShared(CosmeticSlot s) => !IsLimb(s);

        public static string Label(CosmeticSlot s)
        {
            switch (s)
            {
                case CosmeticSlot.Legs:  return "Legs";
                case CosmeticSlot.Arms:  return "Arms";
                case CosmeticSlot.Cloak: return "Cloak";
                default:                 return "Head";
            }
        }
    }

    [System.Serializable]
    public class CosmeticDef
    {
        [Tooltip("Stable and never reused. This is what gets saved into the " +
                 "profile and what goes over the wire, so renaming one orphans " +
                 "every purchase of it.")]
        public string Id;
        public string Name;
        public CosmeticSlot Slot;
        public int Price;
        [Tooltip("Spawned under the limb's bone when worn. May be left null " +
                 "while the art does not exist -- buying, equipping and the " +
                 "role matching all still work, it just puts nothing on the body.")]
        public GameObject Prefab;
    }

    /// The shop's stock.
    ///
    /// EMPTY ON PURPOSE. The stock list is an asset (Resources/CoatShopStock)
    /// so items can be added in the inspector later without touching code, and
    /// every screen below is written to read zero items as a normal state
    /// rather than a bug.
    public static class CoatCatalogue
    {
        static CoatShopStock _stock;
        static bool _looked;

        public static CoatShopStock Stock
        {
            get
            {
                if (!_looked)
                {
                    _stock = Resources.Load<CoatShopStock>("CoatShopStock");
                    _looked = true;
                }
                return _stock;
            }
        }

        /// Forget the cached asset. The editor keeps statics alive across play
        /// sessions, so a test that edits the stock needs a way to re-read it.
        public static void Forget() { _stock = null; _looked = false; }

        /// Swap in a stock list without an asset on disk, so the role
        /// matching can be tested while the real shop is still empty.
        public static void UseForTesting(CoatShopStock s) { _stock = s; _looked = true; }

        public static IReadOnlyList<CosmeticDef> All =>
            Stock != null && Stock.Items != null
                ? (IReadOnlyList<CosmeticDef>)Stock.Items
                : System.Array.Empty<CosmeticDef>();

        public static List<CosmeticDef> InSlot(CosmeticSlot slot)
        {
            var found = new List<CosmeticDef>();
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Slot == slot) found.Add(all[i]);
            return found;
        }

        public static int CountIn(CosmeticSlot slot)
        {
            int n = 0;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Slot == slot) n++;
            return n;
        }

        public static CosmeticDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Id == id) return all[i];
            return null;
        }
    }
}
