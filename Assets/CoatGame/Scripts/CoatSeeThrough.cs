using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coat
{
    /// Anything standing between the camera and the people it is watching goes see
    /// through while it is in the way, and fades back in once it is not.
    ///
    /// This replaces hiding named walls outright. Switching a renderer off works, but
    /// it has to be told which walls, it pops, and the van stops looking like a van.
    /// Probing for whatever is actually in the way means it covers the van, the
    /// doorway, the steps and anything built later, without a list.
    ///
    /// The level's materials are shared assets saved on disk, so nothing here ever
    /// touches them -- each blocked renderer gets its own transparent clone and its
    /// originals are put back when it clears.
    [DefaultExecutionOrder(200)]
    public class CoatSeeThrough : MonoBehaviour
    {
        public CoatGame Game;
        public Camera Eye;

        [Tooltip("How solid a wall is left when it is in the way. 0.2 is the eighty " +
                 "per cent transparent the brief asked for -- enough to still read as " +
                 "a wall, not enough to lose anybody behind it.")]
        [Range(0f, 1f)] public float Alpha = 0.2f;

        [Tooltip("Fattens the probe, so a wall edge does not flicker in and out as " +
                 "somebody walks along behind it.")]
        public float Probe = 0.55f;

        [Tooltip("How fast it fades, in units of alpha per second. Snapping straight " +
                 "to clear is jarring when you walk behind something.")]
        public float FadeSpeed = 6f;

        [Tooltip("Never fade anything nearer the person than this, so the thing they " +
                 "are standing on or next to stays solid.")]
        public float Slack = 0.35f;

        class Ghost
        {
            public Renderer R;
            public Material[] Solid;
            public Material[] Clear;
            public ShadowCastingMode Shadow;
            public float A = 1f;
            public bool Blocking;
        }

        readonly Dictionary<Renderer, Ghost> _ghosts = new Dictionary<Renderer, Ghost>();
        readonly List<Renderer> _done = new List<Renderer>();
        readonly List<Transform> _watching = new List<Transform>();

        void LateUpdate()
        {
            if (Eye == null) Eye = Camera.main;
            if (Eye == null) return;

            foreach (var g in _ghosts.Values) g.Blocking = false;

            Collect();
            foreach (var who in _watching) Sweep(who.position);

            Settle(Time.deltaTime);
        }

        /// Everyone the camera is meant to be able to see: the disguise if it is up,
        /// and anybody still walking around on their own legs.
        void Collect()
        {
            _watching.Clear();
            if (Game == null) return;

            var v = Game.Vehicle;
            if (v != null && v.Worn && v.Body != null && v.Body.Torso != null)
                _watching.Add(v.Body.Torso.transform);

            if (Game.Characters == null) return;
            foreach (var c in Game.Characters)
                if (c != null && c.gameObject.activeInHierarchy && c.Body != null)
                    _watching.Add(c.Body.transform);
        }

        void Sweep(Vector3 target)
        {
            Vector3 eye = Eye.transform.position;
            Vector3 gap = target - eye;
            float far = gap.magnitude - Slack;
            if (far <= 0.01f) return;

            var hits = Physics.SphereCastAll(eye, Probe, gap.normalized, far,
                                             CoatLayers.NotRig, QueryTriggerInteraction.Ignore);

            foreach (var h in hits)
            {
                var r = h.collider.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Mark(r);
            }
        }

        void Mark(Renderer r)
        {
            if (_ghosts.TryGetValue(r, out var g)) { g.Blocking = true; return; }

            var solid = r.sharedMaterials;
            var clear = new Material[solid.Length];
            for (int i = 0; i < solid.Length; i++)
                clear[i] = solid[i] == null ? null : SeeThrough(solid[i]);

            _ghosts[r] = new Ghost
            {
                R = r,
                Solid = solid,
                Clear = clear,
                Shadow = r.shadowCastingMode,
                A = 1f,
                Blocking = true,
            };
        }

        void Settle(float dt)
        {
            _done.Clear();

            foreach (var g in _ghosts.Values)
            {
                float want = g.Blocking ? Alpha : 1f;
                g.A = Mathf.MoveTowards(g.A, want, FadeSpeed * dt);

                if (!g.Blocking && g.A >= 0.999f) { _done.Add(g.R); continue; }

                foreach (var m in g.Clear) Tint(m, g.A);

                if (g.R.sharedMaterials != g.Clear) g.R.sharedMaterials = g.Clear;

                // A wall you can see through should not still throw a solid shadow.
                var wanted = g.A < 0.95f ? ShadowCastingMode.Off : g.Shadow;
                if (g.R.shadowCastingMode != wanted) g.R.shadowCastingMode = wanted;
            }

            foreach (var r in _done) Restore(r);
        }

        void Restore(Renderer r)
        {
            if (!_ghosts.TryGetValue(r, out var g)) return;

            if (g.R != null)
            {
                g.R.sharedMaterials = g.Solid;
                g.R.shadowCastingMode = g.Shadow;
            }
            foreach (var m in g.Clear) if (m != null) Destroy(m);

            _ghosts.Remove(r);
        }

        void OnDisable()
        {
            _done.Clear();
            foreach (var r in _ghosts.Keys) _done.Add(r);
            foreach (var r in _done) Restore(r);
        }

        // ---- turning a URP Lit material transparent ----------------------------

        static Material SeeThrough(Material src)
        {
            var m = new Material(src) { name = src.name + " (see through)" };

            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);      // transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);          // alpha blend
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);

            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static void Tint(Material m, float a)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c);
            }
            if (m.HasProperty("_Color"))
            {
                var c = m.GetColor("_Color"); c.a = a; m.SetColor("_Color", c);
            }
        }
    }
}
