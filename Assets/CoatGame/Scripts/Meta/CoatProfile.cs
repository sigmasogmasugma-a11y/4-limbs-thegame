using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Everything the options screen owns. Only things that actually do
    /// something are in here -- a slider wired to nothing is worse than no
    /// slider, because it reads as broken rather than absent.
    [System.Serializable]
    public class CoatSettings
    {
        [Range(0f, 1f)] public float Volume = 0.8f;
        public float CameraSpeed = 90f;
        public bool InvertCamera = false;
        public bool Fullscreen = true;
        public bool VSync = true;
        [Tooltip("The prototype readout in the corner. Off for recording.")]
        public bool ShowHud = true;

        public CoatSettings Copy() => (CoatSettings)MemberwiseClone();

        /// Push these at the engine. Safe to call every time one changes.
        public void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(Volume);
            QualitySettings.vSyncCount = VSync ? 1 : 0;

            // Not in the editor: forcing the game view fullscreen under
            // somebody who is mid-playtest is not a setting being applied,
            // it is the editor being taken away from them.
#if !UNITY_EDITOR
            if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
#endif
        }
    }

    /// One player's saved account: what they have, what they are wearing, and
    /// how they like the game set up.
    ///
    /// Plain fields and no dictionaries, because JsonUtility cannot serialise a
    /// dictionary and this has to survive a round trip to disk unchanged.
    [System.Serializable]
    public class CoatProfile
    {
        public int Coins = 0;

        [Tooltip("Ids of everything bought. Ownership is permanent; equipping " +
                 "is what changes.")]
        public List<string> Owned = new List<string>();

        [Tooltip("Indexed by CosmeticSlot. One equip per slot, empty for none.")]
        public string[] Equipped = new string[CosmeticSlots.Count];

        [Tooltip("Rounds played, oldest first. Only the newest few matter -- " +
                 "CoatRounds uses them to keep what you just played from " +
                 "coming straight back round.")]
        public List<string> RecentRounds = new List<string>();

        public CoatSettings Settings = new CoatSettings();

        /// A profile straight off disk may have been written by an older build
        /// with fewer slots, and indexing past the end would throw on the first
        /// draw of the shop.
        public void Repair()
        {
            Owned ??= new List<string>();
            RecentRounds ??= new List<string>();
            Settings ??= new CoatSettings();

            // A round that has since been deleted must not keep occupying a
            // slot in the history, or it suppresses nothing and just pushes a
            // real round out of the window.
            if (CoatRounds.All.Count > 0)
                RecentRounds.RemoveAll(id => CoatRounds.Find(id) == null);
            if (Equipped == null || Equipped.Length != CosmeticSlots.Count)
            {
                var grown = new string[CosmeticSlots.Count];
                if (Equipped != null)
                    for (int i = 0; i < Equipped.Length && i < grown.Length; i++)
                        grown[i] = Equipped[i];
                Equipped = grown;
            }

            // Drop equips whose item no longer exists, so a removed cosmetic
            // cannot leave a slot pointing at nothing for the rest of time.
            // Only when there IS a catalogue: with the stock asset missing,
            // everything would look deleted and we would wipe a real loadout.
            if (CoatCatalogue.All.Count == 0) return;
            for (int i = 0; i < Equipped.Length; i++)
                if (!string.IsNullOrEmpty(Equipped[i]) && CoatCatalogue.Find(Equipped[i]) == null)
                    Equipped[i] = null;
        }

        public bool Owns(string id) =>
            !string.IsNullOrEmpty(id) && Owned != null && Owned.Contains(id);

        public string EquippedIn(CosmeticSlot slot)
        {
            int i = (int)slot;
            if (Equipped == null || i < 0 || i >= Equipped.Length) return null;
            return string.IsNullOrEmpty(Equipped[i]) ? null : Equipped[i];
        }

        public bool IsEquipped(string id)
        {
            if (string.IsNullOrEmpty(id) || Equipped == null) return false;
            for (int i = 0; i < Equipped.Length; i++) if (Equipped[i] == id) return true;
            return false;
        }

        public bool Buy(CosmeticDef d, out string error)
        {
            error = null;
            if (d == null)                { error = "No such item."; return false; }
            if (Owns(d.Id))               { error = "Already owned.";  return false; }
            if (Coins < d.Price)          { error = "Not enough coins."; return false; }
            Coins -= d.Price;
            Owned.Add(d.Id);
            return true;
        }

        /// Equipping is per slot, and the item's own slot decides which -- so
        /// putting on a second pair of legs replaces the first rather than
        /// stacking.
        public bool Equip(string id, out string error)
        {
            error = null;
            var d = CoatCatalogue.Find(id);
            if (d == null)      { error = "No such item.";   return false; }
            if (!Owns(id))      { error = "Not owned yet.";  return false; }
            Equipped[(int)d.Slot] = id;
            return true;
        }

        public void Unequip(CosmeticSlot slot)
        {
            int i = (int)slot;
            if (Equipped != null && i >= 0 && i < Equipped.Length) Equipped[i] = null;
        }
    }

    /// Load and save, and the one profile the menu edits.
    public static class CoatSave
    {
        public static string Path => Application.persistentDataPath + "/coat-profile.json";

        static CoatProfile _current;

        public static CoatProfile Current
        {
            get
            {
                if (_current == null) { _current = Load(); _current.Settings.Apply(); }
                return _current;
            }
        }

        /// Throw away the in-memory copy. Tests want a clean one without
        /// touching the file the player actually owns.
        public static void UseForTesting(CoatProfile p) => _current = p;
        public static void Forget() => _current = null;

        public static CoatProfile Load()
        {
            var p = new CoatProfile();
            try
            {
                if (System.IO.File.Exists(Path))
                {
                    string json = System.IO.File.ReadAllText(Path);
                    if (!string.IsNullOrWhiteSpace(json))
                        p = JsonUtility.FromJson<CoatProfile>(json) ?? new CoatProfile();
                }
            }
            catch (System.Exception e)
            {
                // A corrupt save must not be a hard stop at the front door.
                Debug.LogWarning("[coat] could not read the profile, starting fresh: " + e.Message);
                p = new CoatProfile();
            }
            p.Repair();
            return p;
        }

        public static bool Save(CoatProfile p = null)
        {
            p ??= Current;
            try
            {
                System.IO.File.WriteAllText(Path, JsonUtility.ToJson(p, true));
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[coat] could not write the profile: " + e.Message);
                return false;
            }
        }
    }
}
