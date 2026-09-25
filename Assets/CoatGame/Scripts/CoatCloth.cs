using UnityEngine;

namespace Coat
{
    /// The garment itself. A procedural tube hung from the coat's collar, whose centre
    /// line is a little Verlet chain so the hem swings and lags instead of being welded
    /// on. Whoever is inside pushes it out from within.
    ///
    /// This is doing the most important job in the game: four people have to read as one
    /// person from the outside. Nothing else sells that.
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class CoatCloth : MonoBehaviour
    {
        public TheCoat Coat;

        [Header("Shape")]
        public int Rings = 13;
        public int Segments = 20;
        [Tooltip("How many rings are worn tight down the collar-to-waist line. The rest " +
                 "hang free. Too few and the whole chest is one segment and it comes out " +
                 "as a bell instead of a garment.")]
        public int TorsoRings = 5;
        [Tooltip("How far the hem hangs BELOW the waist, not the total coat length.")]
        public float SkirtDrop = 0.50f;
        public float CollarRadius = 0.22f;
        public float ChestRadius = 0.40f;
        public float WaistRadius = 0.38f;
        public float HemRadius = 0.52f;

        [Header("Motion")]
        [Range(0f, 1f)] public float Damping = 0.86f;
        public float Gravity = -13f;
        [Range(1, 12)] public int Relax = 6;
        public float FloorClearance = 0.06f;

        [Header("Occupants")]
        [Tooltip("Bodies inside the coat that hold the cloth out.")]
        public Rigidbody[] Occupants = new Rigidbody[0];
        public float Clearance = 0.2f;

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

            _mesh = new Mesh { name = "Coat" };
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

        void LateUpdate()
        {
            Rebuild(Time.deltaTime);
        }

        /// One cloth step. Public so a test harness can drive it without the editor's
        /// own update loop running.
        public void Rebuild(float deltaTime)
        {
            if (Coat == null || Coat.Hub == null || Coat.Collar == null) return;

            float dt = Mathf.Min(deltaTime, 1f / 30f);
            Vector3 collar = Coat.Collar.position;
            Vector3 waist = Coat.Hub.position;

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
            Vector3 fwd = Coat.Facing;
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

                    p = PushOffOccupants(p, dir);
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

        Vector3 PushOffOccupants(Vector3 p, Vector3 outward)
        {
            if (Occupants == null) return p;

            foreach (var rb in Occupants)
            {
                if (rb == null) continue;

                Vector3 d = p - rb.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m >= Clearance) continue;

                d = m < 1e-4f ? new Vector3(outward.x, 0f, outward.z).normalized : d / m;
                p.x = rb.position.x + d.x * Clearance;
                p.z = rb.position.z + d.z * Clearance;
            }
            return p;
        }
    }
}
