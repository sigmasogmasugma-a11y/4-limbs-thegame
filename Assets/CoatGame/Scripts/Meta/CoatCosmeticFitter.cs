using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Puts worn cosmetics on a rig.
    ///
    /// With an empty catalogue this does nothing at all, which is the intended
    /// state right now -- the point is that the seat-to-limb decision is made
    /// and testable before any art exists, so adding the first hat is a prefab
    /// reference and not a design argument.
    ///
    /// Works on the disguise and on a crew child alike: both name their bodies
    /// ThighL / UpperArmR / Head / Torso, so CoatFit.BoneFor is the only lookup
    /// either one needs.
    public class CoatCosmeticFitter : MonoBehaviour
    {
        [Tooltip("Rig root whose children are the named rigid bodies. Empty " +
                 "uses this object.")]
        public Transform Rig;

        [Tooltip("Cosmetics are parented under a child of the bone rather than " +
                 "the bone itself, so clearing them cannot take anything of the " +
                 "rig's own with it.")]
        public string MountName = "Cosmetics";

        readonly List<GameObject> _spawned = new List<GameObject>();

        public int Worn => _spawned.Count;
        public string LastReport { get; private set; }

        Transform Root => Rig != null ? Rig : transform;

        /// bySeat is indexed by CoatRole -- see CoatLoadout.Resolve.
        public void Dress(CoatProfile[] bySeat) => Dress(CoatLoadout.Resolve(bySeat));

        public void Dress(CoatLoadout.Dressing d)
        {
            Clear();
            if (d == null) { LastReport = "nothing to wear"; return; }

            int put = 0, missingBone = 0, noArt = 0;

            for (int i = 0; i < d.Limbs.Length; i++)
            {
                var w = d.Limbs[i];
                if (!w.Any) continue;
                switch (Put(w.Id, CoatFit.BoneFor(w.Role)))
                {
                    case Result.Placed:      put++; break;
                    case Result.NoBone:      missingBone++; break;
                    case Result.NoPrefab:    noArt++; break;
                }
            }

            foreach (var slot in new[] { CosmeticSlot.Cloak, CosmeticSlot.Head })
            {
                string id = slot == CosmeticSlot.Cloak ? d.Cloak : d.Head;
                if (string.IsNullOrEmpty(id)) continue;
                switch (Put(id, CoatFit.BoneFor(slot)))
                {
                    case Result.Placed:      put++; break;
                    case Result.NoBone:      missingBone++; break;
                    case Result.NoPrefab:    noArt++; break;
                }
            }

            LastReport = $"{put} placed, {noArt} owned but no art yet, " +
                         $"{missingBone} with no such bone";
        }

        enum Result { Placed, NoBone, NoPrefab }

        Result Put(string id, string bone)
        {
            var def = CoatCatalogue.Find(id);
            if (def == null || def.Prefab == null) return Result.NoPrefab;

            Transform t = Find(bone);
            if (t == null) return Result.NoBone;

            Transform mount = t.Find(MountName);
            if (mount == null)
            {
                var go = new GameObject(MountName);
                mount = go.transform;
                mount.SetParent(t, false);
            }

            var made = Instantiate(def.Prefab, mount);
            made.name = def.Id;
            _spawned.Add(made);
            return Result.Placed;
        }

        Transform Find(string bone)
        {
            var root = Root;
            if (root == null) return null;
            // Direct child first: the ragdoll is flat, every body hangs off the
            // root with no hierarchy between them.
            var direct = root.Find(bone);
            if (direct != null) return direct;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == bone) return t;
            return null;
        }

        public void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] == null) continue;
                if (Application.isPlaying) Destroy(_spawned[i]);
                else DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();
        }

        void OnDisable() => Clear();
    }
}
