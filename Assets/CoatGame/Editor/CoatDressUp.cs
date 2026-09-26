using System.Text;
using UnityEngine;
using UnityEditor;
using Coat;

/// Puts the scanned mesh on the ragdoll.
///
/// The rig is built out of capsules and boxes, which is fine for physics and
/// unreadable as a character. This hangs the skinned mesh on it and switches
/// the primitives' renderers off -- the colliders stay exactly as they were, so
/// nothing about the physics changes.
///
/// Menu: Coat > Dress The Rig.
public static class CoatDressUp
{
    const string Fbx = "Assets/CoatGame/Art/Disguise.fbx";
    const string MatDir = "Assets/CoatGame/Art";

    // Blender's material slot order, which the FBX preserves as submesh order.
    // The first five are limb slots and all get the SAME material: the limb
    // colours are in the vertex colours, not in the materials. The eyes are
    // flat, because they are separate geometry joined in after the colour
    // attribute was made and so carry no colour of their own.
    /// Characters are toon shaded; see CoatToon.shader.
    const string Toon = "Coat/Toon";

    const string CrewFbx = "Assets/CoatGame/Art/RedChild.fbx";

    [MenuItem("Coat/Dress The Rig")]
    public static void FromMenu() => Debug.Log(Run() + "\n" + DressCrew());

    /// The four in the van. Each keeps its own colour: the body is one solid
    /// material so it can be tinted per player, which is the whole point of
    /// having four of them.
    public static string DressCrew()
    {
        var log = new StringBuilder();
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(CrewFbx);
        if (src == null) return "No " + CrewFbx + ".";

        // Snapshot the list FIRST. Destroying the old Skin child inside a
        // foreach over every Transform in the scene invalidates the iteration
        // and throws MissingReferenceException partway through, leaving some
        // crew dressed and some not.
        var crews = new System.Collections.Generic.List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.StartsWith("Person_")) crews.Add(t);

        int done = 0;
        foreach (var t in crews)
        {
            if (t == null) continue;

            var old = t.Find("Skin");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var had = t.GetComponent<CoatSkin>();
            if (had != null) { had.ShowPrimitives(); Object.DestroyImmediate(had); }

            // Each child wears the colour of the limb it IS. Person_LeftLeg is
            // the disguise's left leg, so it is the same green -- that is the
            // only cue telling you which body part you are about to become, and
            // which one is missing when a coat comes up short.
            string suffix = t.name.Substring("Person_".Length);
            Color tint = LimbColour(suffix);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = "Skin";
            go.transform.SetParent(t, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.updateWhenOffscreen = true;

            var body = MakeMaterial("CoatCrew" + suffix, Toon, tint, LimbShade(suffix));
            // By measurement, not by index: the FBX permutes material slots,
            // so slot 0 is not reliably the body. A crew member is one solid
            // colour with eyes, so all that matters is telling the eyes apart
            // from everything else.
            var mats = Classify(smr.sharedMesh, out string[] crewOwners);
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name != "CoatSkinEye"
                                    && mats[i].name != "CoatSkinPupil")
                    mats[i] = body;
            smr.sharedMaterials = mats;

            var skin = t.gameObject.AddComponent<CoatSkin>();
            skin.Rig = t;
            skin.Skin = smr;
            skin.HidePrimitives = true;
            // A crew member is either there or not there; it never loses a limb,
            // so every submesh always draws.
            skin.Invisible = MakeMaterial("CoatSkinHidden", "Coat/Hidden", Color.clear);
            skin.SubmeshOwner = new string[mats.Length];
            for (int i = 0; i < mats.Length; i++) skin.SubmeshOwner[i] = "";
            skin.Bind();

            log.AppendLine($"{t.name}: {skin.LastReport}");
            done++;
        }

        if (done == 0) return "No Person_* rigs in the scene.";
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        return log.ToString();
    }

    public static string Run()
    {
        var log = new StringBuilder();

        var rig = GameObject.Find("ClassicRig");
        if (rig == null)
        {
            // It is switched off by default -- the four-people rig is what you
            // land in -- so Find misses it.
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "ClassicRig") { rig = t.gameObject; break; }
        }
        if (rig == null) return "No ClassicRig in the scene. Build the test scene first.";

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (src == null) return "No " + Fbx + ". Run Art/rig_for_unity.py in Blender.";

        // Off with the old one, if this is a re-run.
        var old = rig.transform.Find("Skin");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var already = rig.GetComponent<CoatSkin>();
        if (already != null)
        {
            already.ShowPrimitives();
            Object.DestroyImmediate(already);
        }

        var skinGo = (GameObject)PrefabUtility.InstantiatePrefab(src);
        skinGo.name = "Skin";
        skinGo.transform.SetParent(rig.transform, false);
        skinGo.transform.localPosition = Vector3.zero;
        skinGo.transform.localRotation = Quaternion.identity;

        var smr = skinGo.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) return "The FBX has no SkinnedMeshRenderer.";

        // A skinned mesh whose bones are teleported every frame gets culled the
        // moment its authored bounds leave the camera, because Unity does not
        // recompute them. Let it update them itself.
        smr.updateWhenOffscreen = true;

        // WORK OUT what each submesh is by MEASURING it. Do not trust the
        // order.
        //
        // Blender's slot order is body, armR, armL, legR, legL, eye, pupil.
        // The FBX exporter does not preserve it: Unity gets armR, legR, body,
        // legL, armL, eye, pupil. So indexing colours and owners by slot number
        // painted the torso's colour onto an arm and gave a leg's mesh to an
        // arm's rigid body -- put the left leg in and nothing appeared, put the
        // right arm in and a leg did.
        //
        // It looked correct in every log because the logs printed the material
        // names THIS CODE had just assigned by index, not what the geometry was.
        //
        // Coat/Toon throughout (Frog Sqwad's look), not the old vertex-colour
        // shader: that one drew nothing in the Game view. The project has
        // RequireDepthTexture on, so URP runs a depth prepass, and the shader
        // borrowed UsePass ".../Lit/DepthOnly" -- a pass written against URP's own
        // Lit material. Coat/Toon writes its own DepthOnly and DepthNormals.
        var mats = Classify(smr.sharedMesh, out string[] owners);
        smr.sharedMaterials = mats;

        var skin = rig.AddComponent<CoatSkin>();
        skin.Rig = rig.transform;
        skin.Skin = smr;
        skin.HidePrimitives = true;
        skin.Invisible = MakeMaterial("CoatSkinHidden", "Coat/Hidden", Color.clear);
        skin.SubmeshOwner = owners;
        skin.Bind();

        for (int i = 0; i < mats.Length; i++)
            log.AppendLine($"  submesh {i}: {mats[i].name}" +
                           (string.IsNullOrEmpty(owners[i]) ? " (always drawn)"
                                                            : $" <- {owners[i]}"));
        log.AppendLine("dressed ClassicRig");
        log.AppendLine("  " + skin.LastReport);
        // Do NOT read colors32 here. The imported mesh is not marked readable,
        // so touching it throws "Not allowed to access colors on mesh" into the
        // console. Nothing needs the vertex colours any more anyway.
        log.AppendLine($"  {smr.sharedMesh.vertexCount} verts, {mats.Length} submeshes");

        var b = smr.bounds;
        log.AppendLine($"  world bounds centre {b.center} size {b.size}");

        EditorUtility.SetDirty(rig);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.scene);
        return log.ToString();
    }

    /// Work out what each submesh IS, from where its vertices are.
    ///
    /// Needed because the FBX export permutes the material slots, so the index
    /// tells you nothing. Mesh data arrives Z-up, so z is height and x is the
    /// side; the -90 X rotation the importer puts on the object does not flip
    /// x, so mesh x and Unity world x agree.
    ///
    /// Unity +X is the rig's RIGHT (UpperArmR sits at +0.20), because the FBX
    /// round trip mirrors X against Blender.
    /// The four player colours, in one place.
    ///
    /// Classify() paints the disguise's limbs from these and DressCrew tints
    /// each child from the same table, so a child is always exactly the colour
    /// of the limb it controls.
    ///
    /// The values themselves live in CoatPalette (Frog Sqwad's palette), which
    /// the menus and HUDs read too.
    public static Color LimbColour(string which)
    {
        switch (which)
        {
            case "RightArm": return CoatPalette.RightArm;   // coral red
            case "LeftArm":  return CoatPalette.LeftArm;    // cyan
            case "RightLeg": return CoatPalette.RightLeg;   // orange
            case "LeftLeg":  return CoatPalette.LeftLeg;    // lime
            default:         return CoatPalette.Trenchcoat; // body
        }
    }

    /// The toon shadow tone that goes with LimbColour.
    public static Color LimbShade(string which)
    {
        switch (which)
        {
            case "RightArm": return CoatPalette.RightArmShade;
            case "LeftArm":  return CoatPalette.LeftArmShade;
            case "RightLeg": return CoatPalette.RightLegShade;
            case "LeftLeg":  return CoatPalette.LeftLegShade;
            default:         return CoatPalette.TrenchcoatShade;
        }
    }

    static Material[] Classify(Mesh mesh, out string[] owners)
    {
        int n = mesh.subMeshCount;
        var v = mesh.vertices;
        float top = 0f;
        for (int i = 0; i < v.Length; i++) if (v[i].z > top) top = v[i].z;

        var cx = new float[n];
        var up = new float[n];
        var spread = new float[n];
        for (int s = 0; s < n; s++)
        {
            var t = mesh.GetTriangles(s);
            if (t.Length == 0) continue;
            double sx = 0, sz = 0;
            for (int i = 0; i < t.Length; i++) { sx += v[t[i]].x; sz += v[t[i]].z; }
            cx[s] = (float)(sx / t.Length);
            up[s] = top > 0f ? (float)(sz / t.Length) / top : 0f;

            double d = 0;
            var c = new Vector3(cx[s], 0f, up[s] * top);
            for (int i = 0; i < t.Length; i++)
                d += new Vector2(v[t[i]].x - c.x, v[t[i]].z - c.z).magnitude;
            spread[s] = (float)(d / t.Length);
        }

        // The eyes sit highest. Of the two, the tighter one is the pupil --
        // they share a centroid, so nothing else separates them.
        int eye = -1, pupil = -1;
        for (int s = 0; s < n; s++)
        {
            if (up[s] < 0.85f) continue;
            if (eye < 0) { eye = s; continue; }
            if (spread[s] < spread[eye]) { pupil = eye; eye = s; }
            else if (pupil < 0 || spread[s] < spread[pupil]) pupil = s;
        }
        if (pupil >= 0 && spread[pupil] > spread[eye]) { int t2 = eye; eye = pupil; pupil = t2; }

        var mats = new Material[n];
        owners = new string[n];
        for (int s = 0; s < n; s++)
        {
            if (s == eye)
            {
                // Eye shadow is Frog Sqwad's cool blue-grey, not a darker white.
                mats[s] = MakeMaterial("CoatSkinEye", Toon, CoatPalette.EyeWhite,
                                       new Color(0.714f, 0.796f, 0.831f), outline: 1.4f);
                owners[s] = "";
            }
            else if (s == pupil)
            {
                mats[s] = MakeMaterial("CoatSkinPupil", Toon, CoatPalette.Pupil, CoatPalette.Pupil, outline: 0f);
                owners[s] = "";
            }
            else if (Mathf.Abs(cx[s]) < 0.06f)
            {
                mats[s] = MakeMaterial("CoatSkinBody", Toon, CoatPalette.Trenchcoat, CoatPalette.TrenchcoatShade);
                owners[s] = "";              // the trunk is always aboard
            }
            else
            {
                bool right = cx[s] > 0f;     // +X is the rig's R
                bool leg = up[s] < 0.45f;
                string role = (right ? "Right" : "Left") + (leg ? "Leg" : "Arm");
                mats[s] = MakeMaterial("CoatSkin" + (leg ? "Leg" : "Arm") +
                                       (right ? "R" : "L"), Toon, LimbColour(role), LimbShade(role));
                owners[s] = leg ? (right ? "ThighR" : "ThighL")
                                : (right ? "UpperArmR" : "UpperArmL");
            }
        }
        return mats;
    }

    static Material MakeMaterial(string name, string shader, Color colour,
                                 Color? shade = null, float outline = -1f)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (sh == null) { Debug.LogError("no shader " + shader); return null; }

        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = sh;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
        // Toon (Coat/Toon): the shadow tone and the outline travel with the colour.
        if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", shade ?? CoatPalette.Shade(colour));
        if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", CoatPalette.Outline);
        if (outline >= 0f && m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", outline);
        EditorUtility.SetDirty(m);
        return m;
    }
}
