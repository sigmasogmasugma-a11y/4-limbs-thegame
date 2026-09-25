using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// Drapes a skinned mesh over a ragdoll.
    ///
    /// The ragdoll is a pile of loose rigid bodies with no hierarchy between
    /// them -- every one is a direct child of the rig root, held together only
    /// by joints. A SkinnedMeshRenderer wants a bone hierarchy. So rather than
    /// trying to make one drive the other structurally, every frame each bone
    /// is simply put where its body is.
    ///
    /// The two rest poses do NOT have to match. The scan's arms splay out; the
    /// rig's hang straight down at x +-0.20. Binding records where each bone
    /// sits in its body's local space and reproduces exactly that offset from
    /// then on, so the mesh keeps its own proportions and still follows the
    /// physics rigidly.
    [DefaultExecutionOrder(200)]
    public class CoatSkin : MonoBehaviour
    {
        [Tooltip("The ragdoll whose bodies drive the mesh. Bones are matched to " +
                 "rigid bodies BY NAME, so the bone names in the FBX have to be " +
                 "the same as the GameObject names the builder gives the bodies.")]
        public Transform Rig;

        public SkinnedMeshRenderer Skin;

        [Tooltip("Switch off the capsules and boxes the rig is built from once " +
                 "the mesh is on. They are still there and still colliding -- " +
                 "they are just not drawn.")]
        public bool HidePrimitives = true;

        [System.Serializable]
        struct Link
        {
            public Transform Bone;
            public Transform Body;
            public Vector3 Offset;     // bone position in the body's local space
            public Quaternion Twist;   // bone rotation relative to the body
        }

        /// SERIALIZED, and that is the whole point.
        ///
        /// Binding records the gap between each bone and its body AT THAT
        /// MOMENT, so it is only correct if the ragdoll is in the pose the mesh
        /// was authored against. The editor tool binds with the rig at rest,
        /// which is right. Re-binding in Start is not: by then the van has
        /// teleported the bodies to their spawn while the mesh is still at the
        /// rig root, so it baked in a 0.23 m offset plus whatever crouch the
        /// limbs happened to be in -- and the mesh then wore that error for the
        /// rest of the session, 0.53 m adrift at the foot.
        [SerializeField] Link[] _links;

        readonly List<Renderer> _hidden = new List<Renderer>();

        [Header("Limbs that are not aboard")]
        [Tooltip("Material that draws nothing. A limb nobody is wearing has to " +
                 "disappear, and with one skinned renderer for the whole body " +
                 "the only way to drop a limb is to make its submesh invisible.")]
        public Material Invisible;

        [Tooltip("Which rigid body decides whether each submesh is drawn. Index " +
                 "is the submesh; an empty name means always draw. Blender's " +
                 "material order is body, armR, armL, legR, legL, eye, pupil.")]
        public string[] SubmeshOwner =
            { "", "UpperArmR", "UpperArmL", "ThighR", "ThighL", "", "" };

        /// Serialized, and captured at BIND time. Capturing it lazily on first
        /// use would read whatever is on the renderer right then -- and if the
        /// scene was last saved with a limb missing, that is the invisible
        /// material, which would then be remembered as the limb's real look and
        /// the limb would never come back.
        [SerializeField] Material[] _shown;

        bool[] _drawn;          // last pushed, so materials only change on change

        public int Bound => _links == null ? 0 : _links.Length;
        public string LastReport { get; private set; }

        void Start()
        {
            // Only if nobody has bound it yet. A bind baked at rest in the
            // editor is the good one; redoing it here would throw it away and
            // replace it with a bind taken mid-spawn.
            if (_links == null || _links.Length == 0) Bind();
        }

        /// Match bones to bodies by name and record how they sit relative to
        /// each other right now. Anything unmatched is reported rather than
        /// silently skipped -- a typo in a bone name is otherwise invisible
        /// until the mesh does not move.
        public void Bind()
        {
            if (Rig == null || Skin == null || Skin.bones == null)
            {
                LastReport = "no rig or no skin";
                return;
            }

            var bodies = new Dictionary<string, Transform>();
            foreach (var rb in Rig.GetComponentsInChildren<Rigidbody>(true))
                bodies[rb.name] = rb.transform;

            var links = new List<Link>();
            var missing = new List<string>();
            foreach (var bone in Skin.bones)
            {
                if (bone == null) continue;
                if (!bodies.TryGetValue(bone.name, out Transform body))
                {
                    // A crew member has ONE leg -- two of them side by side
                    // under the coat are the disguise's pair -- so its bodies
                    // are Thigh/Shin/Foot with no side. Let ThighL and ThighR
                    // both fall back to it: the mesh's two legs then move in
                    // lockstep off the one chain, which beats a mesh with one
                    // leg nailed on and the other hanging in space.
                    string bare = bone.name.Length > 1 &&
                                  (bone.name[bone.name.Length - 1] == 'L' ||
                                   bone.name[bone.name.Length - 1] == 'R')
                                ? bone.name.Substring(0, bone.name.Length - 1)
                                : null;

                    if (bare == null || !bodies.TryGetValue(bare, out body))
                    {
                        // Not an error. A bone with no body keeps its local
                        // transform, so it rides its parent -- which is how the
                        // crew's upper arms and forearms follow the torso while
                        // only the hand is driven.
                        missing.Add(bone.name);
                        continue;
                    }
                }
                links.Add(new Link
                {
                    Bone = bone,
                    Body = body,
                    Offset = body.InverseTransformPoint(bone.position),
                    Twist = Quaternion.Inverse(body.rotation) * bone.rotation,
                });
            }
            _links = links.ToArray();
            _shown = Skin.sharedMaterials;
            _drawn = null;

            if (HidePrimitives) Hide(bodies);

            LastReport = $"bound {_links.Length} of {Skin.bones.Length} bones" +
                         (missing.Count > 0 ? "; no body for " + string.Join(", ", missing) : "");
        }

        void Hide(Dictionary<string, Transform> bodies)
        {
            foreach (var body in bodies.Values)
            {
                var r = body.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
        }

        public void ShowPrimitives()
        {
            foreach (var r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
        }

        /// LateUpdate, not FixedUpdate: the bodies are interpolated for
        /// rendering, so reading them any earlier puts the mesh a frame behind
        /// the colliders it is meant to be wearing.
        void LateUpdate()
        {
            Apply();
        }

        /// Separate from LateUpdate so a test harness can drive it.
        ///
        /// The harnesses run physics by hand -- simulationMode Script plus
        /// Physics.Simulate in a loop -- and Unity still only calls LateUpdate
        /// once per editor frame. Measuring the bones against the bodies in
        /// that state compares a pose from hundreds of substeps ago with the
        /// current one, which reads as the bind slipping when it is not.
        public void Apply()
        {
            if (_links == null) return;

            Transform home = Home();
            for (int i = 0; i < _links.Length; i++)
            {
                Link l = _links[i];
                if (l.Bone == null || l.Body == null) continue;

                // A limb nobody is wearing has its GameObjects switched off, and
                // the transform of an inactive object still reads fine -- it is
                // simply FROZEN wherever it was left. Driving a bone from one
                // parks that limb's mesh in world space while the body walks
                // away from it: an arm hanging in mid-air, not moving, which is
                // exactly what it looks like. Collapse it onto the pelvis
                // instead, so the worst case is a limb tucked inside the coat
                // rather than one left behind in the street.
                if (home != null && !l.Body.gameObject.activeSelf)
                {
                    l.Bone.position = home.position;
                    l.Bone.rotation = home.rotation;
                    continue;
                }

                l.Bone.position = l.Body.TransformPoint(l.Offset);
                l.Bone.rotation = l.Body.rotation * l.Twist;
            }
            ShowAboardLimbs();
        }

        /// Drop the submeshes whose limb is not aboard.
        ///
        /// SetPresent(false) switches a limb's GameObjects off, which used to be
        /// the whole story: the capsules went away with them. One skinned mesh
        /// does not work like that -- it kept drawing all four limbs however
        /// many people had boarded, so one player alone turned up as a whole
        /// body. The transforms of an inactive object still read fine, so the
        /// bones went on being driven and nothing looked broken from the code's
        /// point of view.
        public void ShowAboardLimbs()
        {
            if (Skin == null || Invisible == null || SubmeshOwner == null) return;

            int n = Skin.sharedMesh.subMeshCount;
            if (_shown == null || _shown.Length != n) return;   // not bound yet
            if (_drawn == null || _drawn.Length != n)
            {
                _drawn = new bool[n];
                for (int i = 0; i < n; i++) _drawn[i] = true;
                Skin.sharedMaterials = _shown;                  // start from clean
            }

            bool changed = false;
            var want = new bool[n];
            for (int i = 0; i < n; i++)
            {
                string owner = i < SubmeshOwner.Length ? SubmeshOwner[i] : "";
                want[i] = string.IsNullOrEmpty(owner) || Aboard(owner);
                if (want[i] != _drawn[i]) changed = true;
            }
            if (!changed) return;

            // Only when it actually changes: assigning sharedMaterials allocates.
            var mats = new Material[n];
            for (int i = 0; i < n; i++)
            {
                mats[i] = want[i] ? _shown[i] : Invisible;
                _drawn[i] = want[i];
            }
            Skin.sharedMaterials = mats;
        }

        Transform _home;

        /// The pelvis: the one body that is always aboard, and so the only safe
        /// place to put a limb that is not.
        Transform Home()
        {
            if (_home != null) return _home;
            if (_links == null) return null;
            for (int i = 0; i < _links.Length; i++)
                if (_links[i].Body != null && _links[i].Body.name == "Pelvis")
                    return _home = _links[i].Body;
            return null;
        }

        bool Aboard(string bodyName)
        {
            for (int i = 0; i < _links.Length; i++)
                if (_links[i].Body != null && _links[i].Body.name == bodyName)
                    // activeSelf, NOT activeInHierarchy. SetPresent toggles the
                    // limb's own flag; activeInHierarchy is also false whenever
                    // the rig ROOT is off, which is any time the coat is not
                    // being worn -- so it reported every limb missing and hid
                    // the one that was actually there.
                    return _links[i].Body.gameObject.activeSelf;
            return true;
        }
    }
}
