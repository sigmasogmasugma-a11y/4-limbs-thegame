using UnityEngine;

namespace Coat.Classic
{
    /// The coat on the original single body. A procedural tube whose centre line is a
    /// little Verlet chain, so the hem swings and lags instead of being welded on, with
    /// the legs pushing it out from the inside.
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ClassicCloth : MonoBehaviour
    {
        public ClassicRagdoll Body;

        [Header("Shape")]
        public int Rings = 13;
        public int Segments = 18;
        [Tooltip("Rings worn tight down the collar-to-waist line. Too few and the whole " +
                 "chest is one segment and it comes out as a bell instead of a garment.")]
        public int TorsoRings = 5;
        [Tooltip("Collar height above the torso's centre.")]
        public float CollarHeight = 0.26f;
        [Tooltip("How far the hem hangs BELOW the waist, not the total coat length.")]
        public float SkirtDrop = 0.5f;
        public float CollarRadius = 0.17f;
        public float ChestRadius = 0.3f;
        public float WaistRadius = 0.26f;
        public float HemRadius = 0.38f;

        [Header("Motion")]
        [Range(0f, 1f)] public float Damping = 0.86f;
        public float Gravity = -13f;
        [Range(1, 12)] public int Relax = 6;
        public float FloorClearance = 0.06f;

        [Header("Legs")]
        public float LegClearance = 0.115f;

        Mesh _mesh;
        Vector3[] _node, _prev, _verts;
        bool _seeded;

        void Awake()
        {
            Rings = Mathf.Max(4, Rings);
            Segments = Mathf.Max(6, Segments);
            TorsoRings = Mathf.Clamp(TorsoRings, 2, Rings - 2);

            _node = new Vector3[Rings];
            _prev = new Vector3[Rings];
            _verts = new Vector3[Rings * Segments];

            _mesh = new Mesh { name = "ClassicCoat" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.triangles = BuildTriangles();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        int[] BuildTriangles()
        {
            var tris = new int[(Rings - 1) * Segments * 6];
            int t = 0;

            for (int i = 0; i < Rings - 1; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = i * Segments + j;
                    int b = i * Segments + (j + 1) % Segments;
                    int c = (i + 1) * Segments + j;
                    int d = (i + 1) * Segments + (j + 1) % Segments;

                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }
            return tris;
        }

        void LateUpdate() => Rebuild(Time.deltaTime);

        /// Forget where the hem was. Without this, standing the body up somewhere new
        /// leaves the skirt strung out between here and wherever it last hung.
        public void Reseed() => _seeded = false;

        public void Rebuild(float deltaTime)
        {
            if (Body == null || Body.Torso == null || Body.Pelvis == null) return;

            float dt = Mathf.Min(deltaTime, 1f / 30f);
            Vector3 collar = Body.Torso.position + Body.Torso.transform.up * CollarHeight;
            Vector3 waist = Body.Pelvis.position;

            if (!_seeded) { Seed(collar, waist); _seeded = true; }

            Simulate(collar, waist, dt);
            Shape();

            _mesh.vertices = _verts;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        void Seed(Vector3 collar, Vector3 waist)
        {
            WearTorso(collar, waist);
            float seg = SegmentLength;
            for (int i = TorsoRings; i < Rings; i++)
            {
                _node[i] = waist + Vector3.down * (seg * (i - TorsoRings + 1));
                _prev[i] = _node[i];
            }
        }

        void WearTorso(Vector3 collar, Vector3 waist)
        {
            for (int i = 0; i < TorsoRings; i++)
            {
                Vector3 p = Vector3.Lerp(collar, waist, i / (TorsoRings - 1f));
                _node[i] = _prev[i] = p;
            }
        }

        float SegmentLength => SkirtDrop / Mathf.Max(1, Rings - TorsoRings);

        void Simulate(Vector3 collar, Vector3 waist, float dt)
        {
            WearTorso(collar, waist);

            for (int i = TorsoRings; i < Rings; i++)
            {
                Vector3 v = (_node[i] - _prev[i]) * Damping;
                _prev[i] = _node[i];
                _node[i] += v + Vector3.up * (Gravity * dt * dt);
            }

            float seg = SegmentLength;
            for (int k = 0; k < Relax; k++)
            {
                for (int i = TorsoRings; i < Rings; i++)
                {
                    Vector3 d = _node[i] - _node[i - 1];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    _node[i] -= d * (1f - seg / len);
                }

                for (int i = TorsoRings; i < Rings; i++)
                    if (_node[i].y < FloorClearance) _node[i].y = FloorClearance;
            }
        }

        void Shape()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(Body.Pelvis.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            for (int i = 0; i < Rings; i++)
            {
                float r = RadiusAt(i);
                Vector3 centre = _node[i];

                for (int j = 0; j < Segments; j++)
                {
                    float a = j / (float)Segments * Mathf.PI * 2f;
                    Vector3 dir = right * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                    Vector3 p = centre + dir * r;

                    if (i >= TorsoRings - 1) p = PushOffLegs(p, dir);
                    _verts[i * Segments + j] = transform.InverseTransformPoint(p);
                }
            }
        }

        /// Keyed off ring index rather than height, with the waist pinned to the last
        /// worn ring, so the profile lands on the body wherever the rings happen to be.
        float RadiusAt(int i)
        {
            float waistRing = TorsoRings - 1f;

            if (i <= waistRing)
            {
                float u = waistRing < 1f ? 1f : i / waistRing;
                return u < 0.5f
                    ? Mathf.Lerp(CollarRadius, ChestRadius, u / 0.5f)
                    : Mathf.Lerp(ChestRadius, WaistRadius, (u - 0.5f) / 0.5f);
            }

            float v = (i - waistRing) / Mathf.Max(1f, Rings - 1 - waistRing);
            return Mathf.Lerp(WaistRadius, HemRadius, v);
        }

        Vector3 PushOffLegs(Vector3 p, Vector3 outward)
        {
            // A leg whose player is not aboard is switched off, and its bones are stale.
            if (Body.LegL.Present)
            {
                p = Push(p, outward, Body.LegL.Upper, Body.LegL.UpperLen);
                p = Push(p, outward, Body.LegL.Lower, Body.LegL.LowerLen);
            }
            if (Body.LegR.Present)
            {
                p = Push(p, outward, Body.LegR.Upper, Body.LegR.UpperLen);
                p = Push(p, outward, Body.LegR.Lower, Body.LegR.LowerLen);
            }
            return p;
        }

        /// Bones run along their own local +Y, so the capsule axis is that direction
        /// through the bone's centre.
        Vector3 Push(Vector3 p, Vector3 outward, Rigidbody bone, float len)
        {
            if (bone == null) return p;

            Vector3 axis = bone.rotation * Vector3.up;
            Vector3 a = bone.position - axis * (len * 0.5f);
            Vector3 b = bone.position + axis * (len * 0.5f);
            Vector3 near = ClosestOnSegment(a, b, p);

            Vector3 d = p - near;
            d.y = 0f;
            float m = d.magnitude;
            if (m >= LegClearance) return p;

            d = m < 1e-4f ? new Vector3(outward.x, 0f, outward.z).normalized : d / m;
            p.x = near.x + d.x * LegClearance;
            p.z = near.z + d.z * LegClearance;
            return p;
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float sq = ab.sqrMagnitude;
            if (sq < 1e-6f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / sq);
        }
    }
}
