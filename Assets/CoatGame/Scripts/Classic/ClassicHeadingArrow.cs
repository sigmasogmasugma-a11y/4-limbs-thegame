using UnityEngine;

namespace Coat.Classic
{
    /// Draws where the body is being steered and where it is actually pointing, as two
    /// arrows on the floor. They are different things -- Heading is the intent, and the
    /// upright drive only torques the body towards it -- and telling them apart by eye
    /// is impossible, which is how a "he faces the wrong way" report can go unsolved.
    ///
    /// Green is the heading it is steering to. Orange is where the body actually points.
    public class ClassicHeadingArrow : MonoBehaviour
    {
        public ClassicRagdoll Body;
        public bool Show = true;
        public float Length = 1.1f;
        public float Width = 0.06f;

        LineRenderer _wanted, _actual;

        void Awake()
        {
            _wanted = Make("HeadingWanted", new Color(0.35f, 0.9f, 0.4f));
            _actual = Make("HeadingActual", new Color(1f, 0.55f, 0.15f));
        }

        LineRenderer Make(string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 4;
            lr.widthMultiplier = Width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // Unlit so it reads clearly against the floor whatever the lighting does.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            lr.material = new Material(shader);
            lr.material.color = c;
            lr.startColor = lr.endColor = c;
            return lr;
        }

        void LateUpdate()
        {
            if (Body == null || Body.Pelvis == null) { Enable(false); return; }

            bool on = Show && Body.gameObject.activeInHierarchy;
            Enable(on);
            if (!on) return;

            Vector3 foot = Body.Pelvis.position;
            foot.y = 0.03f;

            Draw(_wanted, foot, Body.Heading);
            Draw(_actual, foot + Vector3.up * 0.01f, Body.Pelvis.transform.forward);
        }

        void Draw(LineRenderer lr, Vector3 from, Vector3 dir)
        {
            dir = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (dir.sqrMagnitude < 1e-5f) { lr.enabled = false; return; }
            dir.Normalize();

            Vector3 tip = from + dir * Length;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (Length * 0.16f);
            Vector3 barb = tip - dir * (Length * 0.22f);

            // Shaft out to the tip, then back down one barb: enough to read as an arrow
            // from any angle without needing a mesh.
            lr.SetPosition(0, from);
            lr.SetPosition(1, tip);
            lr.SetPosition(2, barb + side);
            lr.SetPosition(3, tip);
            lr.enabled = true;
        }

        void Enable(bool on)
        {
            if (_wanted != null) _wanted.enabled = on;
            if (_actual != null) _actual.enabled = on;
        }
    }
}
