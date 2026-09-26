using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Coat;

/// Rebuilds the whole prototype from nothing via Coat > Build Test Scene.
/// Four separate little people, one coat lying on the floor with four places in it,
/// and somebody watching. Kept in code rather than a prefab so tuning it is a diff
/// instead of a scene merge conflict.
public static class CoatRagdollBuilder
{
    const string Root = "Assets/CoatGame";
    const string MatDir = Root + "/Materials";

    // One of them, standing on a single articulated leg. Hips and torso are separate
    // bodies on a springy joint, so the upper half bends and lags behind the legs.
    const float PelvisY = 0.52f, TorsoY = 0.74f, HeadY = 0.97f, HandY = 0.78f;
    const float HipY = 0.45f, KneeY = 0.24f, KneeZ = 0.06f, AnkleY = 0.06f;
    const float CoatY = 0.86f;

    static readonly Vector3 CoatSpot = new Vector3(0f, CoatY, -1.4f);

    /// The van faces +Z, which is where the level is. Everything else in the scene is
    /// laid out on that side, so the crew walks out of the back of it into the job.
    static readonly Vector3 VanSpot = new Vector3(0f, 0f, -1.9f);

    [MenuItem("Coat/Build Test Scene")]
    public static void Build()
    {
        EnsureLayer(CoatLayers.Rig, "Coat");

        DestroyIfExists("CoatGame");
        DestroyIfExists("Van");
        DestroyIfExists("TheJob");
        DestroyIfExists("TestLevel");
        DestroyIfExists("Observer");
        DestroyIfExists("ClassicRig");
        DestroyIfExists("ClassicObserver");
        DestroyIfExists("CoatRig");   // the pre-split rig, if an old scene still has it

        BuildLevel();

        var root = new GameObject("CoatGame");
        var input = root.AddComponent<LocalCoatInput>();
        var game = root.AddComponent<CoatGame>();
        game.Input = input;

        var coat = BuildCoat(root.transform);
        game.Coat = coat;

        var people = new CoatCharacter[4];
        Color[] tint =
        {
            new Color(0.28f, 0.55f, 0.86f),   // left leg
            new Color(0.88f, 0.47f, 0.28f),   // right leg
            new Color(0.45f, 0.78f, 0.42f),   // left arm
            new Color(0.80f, 0.42f, 0.78f),   // right arm
        };
        string[] names = { "LeftLeg", "RightLeg", "LeftArm", "RightArm" };

        // Seated in the back of the van, two a side, facing the coat on the floor
        // between them. Kept in step with CoatVan.Seats.
        Vector3[] seats =
        {
            new Vector3(-0.6f, 0f,  1.2f),
            new Vector3( 0.6f, 0f,  1.2f),
            new Vector3(-0.6f, 0f, -0.4f),
            new Vector3( 0.6f, 0f, -0.4f),
        };

        for (int i = 0; i < 4; i++)
            people[i] = BuildCharacter(root.transform, names[i], (CoatRole)i,
                                       VanSpot + seats[i], tint[i]);

        game.Characters = people;

        // The two at the bottom are what the cloth has to hang off.
        var cloth = coat.GetComponentInChildren<CoatCloth>();
        cloth.Occupants = new[] { people[0].Body, people[1].Body };

        var van = BuildVan(game, coat);
        game.Van = van;

        var loot = BuildJob(game, van);
        game.Loot = loot;
        van.Loot = loot;

        var observer = BuildObserver(coat, people);
        var classic = BuildClassic();
        var classicObserver = BuildClassicObserver(classic);
        SetupCamera(game, coat, observer, input, classic, classicObserver);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Coat scene built: four people, one coat. Press Play.");
    }

    // ---------- one of the four ----------

    static CoatCharacter BuildCharacter(Transform parent, string name, CoatRole role, Vector3 at, Color tint)
    {
        var suit = Mat("CoatPerson" + name, tint);
        var skin = Mat("CoatSkin", CoatPalette.Skin);

        var go = new GameObject("Person_" + name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;

        var pelvisGo = Prim(PrimitiveType.Cube, "Pelvis", go.transform, at + Vector3.up * PelvisY,
                            new Vector3(0.22f, 0.14f, 0.18f), suit);
        var body = RB(pelvisGo, 4f);

        var torso = Capsule("Torso", go.transform, at + Vector3.up * TorsoY, 0.14f, 0.30f, 5f, suit);
        // Springy, not locked: this is the bend and lag that makes the walk look alive.
        // A capsule's mesh runs from -1 to +1 in its own Y, so its base is -1, not -0.5.
        Hinge(torso, body, new Vector3(0f, -1f, 0f), 26f, 900f, 70f);

        var head = Ball("Head", go.transform, at + Vector3.up * HeadY, 0.24f, 1.4f, skin);
        Hinge(head, torso, new Vector3(0f, -0.5f, 0f), 28f, 420f, 34f);

        // Real arms, not floating hands. They used to be two balls leashed to the
        // torso, which read as a prototype: the hands drifted about on their own
        // and nothing connected them to the shoulders. Jointing them to the
        // torso means they swing with the body for free, and because
        // CoatCharacter drives the HAND with a spring force the whole chain gets
        // dragged along behind it -- so the arm code did not have to change, and
        // grabbing still works because the grab joint is on the hand either way.
        var armL = CrewArm(go.transform, torso, at, -1f, suit, out Rigidbody handL);
        var armR = CrewArm(go.transform, torso, at, 1f, suit, out Rigidbody handR);

        // TWO legs each now. They used to have one apiece -- two crew side by
        // side under the coat being the disguise's pair -- which is fine inside
        // the coat, where the whole character is switched off and the shared
        // body walks, but out in the van it left them hopping on one foot.
        var leg = CrewLeg(go.transform, body, at, -CrewHipX, "L", suit);
        var legR = CrewLeg(go.transform, body, at, CrewHipX, "R", suit);
        var c = go.AddComponent<CoatCharacter>();
        c.Role = role;
        c.Body = body;
        c.Torso = torso;
        c.Head = head;
        c.HandL = handL;
        c.HandR = handR;
        c.Leg = leg;
        c.LegR = legR;
        c.StandHeight = PelvisY - AnkleY;

        SetLayerRecursive(go, CoatLayers.Rig);
        return c;
    }

    /// How far apart a crew member's hips are. Narrow, because they are small
    /// and it keeps the two feet from fouling each other on every stride.
    const float CrewHipX = 0.07f;

    /// One leg of a crew member, mirrored by the sign of x.
    static CoatLeg CrewLeg(Transform t, Rigidbody body, Vector3 at, float x,
                           string side, Material suit)
    {
        Vector3 hip = at + new Vector3(x, HipY, 0f);
        Vector3 knee = at + new Vector3(x, KneeY, KneeZ);
        Vector3 ankle = at + new Vector3(x, AnkleY, 0f);

        var thigh = Bone("Thigh" + side, t, hip, knee, 0.055f, 1.0f, suit, out float thighLen);
        var shin = Bone("Shin" + side, t, knee, ankle, 0.048f, 0.7f, suit, out float shinLen);
        var footGo = Prim(PrimitiveType.Cube, "Foot" + side, t,
                          at + new Vector3(x, 0.04f, 0.05f),
                          new Vector3(0.09f, 0.08f, 0.18f), suit);
        var foot = RB(footGo, 0.4f);

        var hipJoint = Hinge(thigh, body, new Vector3(0f, -1f, 0f), 100f, 3000f, 70f);
        var kneeJoint = Hinge(shin, thigh, new Vector3(0f, -1f, 0f), 140f, 2400f, 55f);
        Hinge(foot, shin, new Vector3(0f, 1f, -0.25f), 40f, 300f, 26f);

        var leg = t.gameObject.AddComponent<CoatLeg>();
        leg.Root = body;
        leg.Thigh = thigh;
        leg.Shin = shin;
        leg.Foot = foot;
        leg.Hip = hipJoint;
        leg.Knee = kneeJoint;
        leg.HipLocal = body.transform.InverseTransformPoint(hip);
        leg.ThighLen = thighLen;
        leg.ShinLen = shinLen;
        leg.AnkleHeight = AnkleY;
        leg.BendSign = -1f;   // MEASURED on the big rig: with +1 the knee sat 29 cm BEHIND
                              // the line from hip to ankle -- a bird's leg, and from the side
                              // it reads as a body facing the other way.
        return leg;
    }

    /// Shoulder to elbow to wrist to hand, hung off the torso.
    static Rigidbody CrewArm(Transform t, Rigidbody torso, Vector3 at, float side,
                             Material suit, out Rigidbody hand)
    {
        string s = side < 0f ? "L" : "R";
        Vector3 shoulder = at + new Vector3(side * 0.155f, 0.76f, 0f);
        Vector3 elbow = at + new Vector3(side * 0.190f, 0.64f, 0f);
        Vector3 wrist = at + new Vector3(side * 0.210f, 0.53f, 0f);

        var upper = Bone("UpperArm" + s, t, shoulder, elbow, 0.040f, 0.35f, suit, out _);
        var lower = Bone("Forearm" + s, t, elbow, wrist, 0.034f, 0.25f, suit, out _);
        hand = Ball("Hand" + s, t, at + new Vector3(side * 0.215f, 0.49f, 0f),
                    0.11f, 0.3f, suit);

        // Loose enough to swing, stiff enough that the arm follows the hand
        // rather than trailing a metre behind it.
        Hinge(upper, torso, new Vector3(0f, -1f, 0f), 95f, 220f, 22f);
        Hinge(lower, upper, new Vector3(0f, -1f, 0f), 90f, 170f, 17f);
        Hinge(hand, lower, new Vector3(0f, 0.45f, 0f), 45f, 90f, 9f);
        return upper;
    }

    // ---------- the coat ----------

    static TheCoat BuildCoat(Transform parent)
    {
        var fabric = Mat("CoatFabric", CoatPalette.Trenchcoat, CoatPalette.TrenchcoatShade);
        // A one-sided tube shows you its own interior from the wrong angle.
        if (fabric.HasProperty("_Cull")) fabric.SetFloat("_Cull", 0f);
        fabric.doubleSidedGI = true;
        EditorUtility.SetDirty(fabric);

        var go = new GameObject("TheCoat");
        go.transform.SetParent(parent, false);
        go.transform.position = CoatSpot;

        // An invisible hub the wearers hang off. It needs a collider so the coat rests
        // on the floor when nobody is inside it.
        var hubGo = new GameObject("Hub");
        hubGo.transform.SetParent(go.transform, false);
        hubGo.transform.position = CoatSpot;
        var box = hubGo.AddComponent<BoxCollider>();
        box.size = new Vector3(0.34f, 0.34f, 0.26f);
        var hub = RB(hubGo, 3f);
        hub.linearDamping = 0.6f;
        hub.angularDamping = 6f;

        var collar = new GameObject("Collar").transform;
        collar.SetParent(hubGo.transform, false);
        collar.localPosition = new Vector3(0f, 0.62f, 0f);

        var hem = new GameObject("Hem").transform;
        hem.SetParent(hubGo.transform, false);
        hem.localPosition = new Vector3(0f, -0.50f, 0f);

        // The one head the disguise shows the world. The riders hide their own.
        var skin = Mat("CoatSkin", CoatPalette.Skin);
        var face = Prim(PrimitiveType.Sphere, "DisguiseHead", collar,
                        collar.position + Vector3.up * 0.16f, Vector3.one * 0.3f, skin);
        face.SetActive(false);

        var coat = go.AddComponent<TheCoat>();
        // Occupancy only. CoatVehicle stands the real body up once all four are aboard.
        coat.Assemble = false;
        coat.Hub = hub;
        coat.Collar = collar;
        coat.Hem = hem;
        coat.DisguiseHead = face;

        var clothGo = new GameObject("Cloth");
        clothGo.transform.SetParent(go.transform, false);
        clothGo.transform.localPosition = Vector3.zero;
        clothGo.transform.localRotation = Quaternion.identity;
        clothGo.AddComponent<MeshFilter>();
        clothGo.AddComponent<MeshRenderer>().sharedMaterial = fabric;
        var cloth = clothGo.AddComponent<CoatCloth>();
        cloth.Coat = coat;

        SetLayerRecursive(go, CoatLayers.Rig);
        return coat;
    }

    // ---------- the level ----------

    static void BuildLevel()
    {
        var ground = Mat("CoatGround", CoatPalette.Road);
        var prop = Mat("CoatProp", CoatPalette.Wood);
        var trim = Mat("CoatTrim", CoatPalette.Doorway);
        var steps = Mat("CoatStep", CoatPalette.Pavement);
        // Frog Sqwad marks every drop with yellow and black stripes. The kerb is
        // a cube stretched to 11 x 0.5 m, so the stripe tile repeats 22 times
        // along it to stay square.
        var kerb = Mat("CoatKerb", Color.white);
        var hazard = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CoatGame/Art/CoatHazard.png");
        if (hazard != null && kerb.HasProperty("_BaseMap"))
        {
            kerb.SetTexture("_BaseMap", hazard);
            kerb.SetTextureScale("_BaseMap", new Vector2(22f, 1f));
            EditorUtility.SetDirty(kerb);
        }

        var root = new GameObject("TestLevel");
        var t = root.transform;

        Prim(PrimitiveType.Cube, "Ground", t, new Vector3(0f, -0.5f, 0f), new Vector3(44f, 1f, 44f), ground);
        Prim(PrimitiveType.Cube, "Kerb", t, new Vector3(0f, 0.07f, 5.5f), new Vector3(11f, 0.14f, 0.5f), kerb);

        for (int i = 0; i < 3; i++)
        {
            float h = 0.20f + i * 0.20f;
            Prim(PrimitiveType.Cube, "Step" + i, t, new Vector3(-5.5f, h * 0.5f, 8.2f + i * 0.7f), new Vector3(4f, h, 0.7f), steps);
        }

        Prim(PrimitiveType.Cube, "JambL", t, new Vector3(4.3f, 1.1f, 8.2f), new Vector3(0.3f, 2.2f, 0.35f), trim);
        Prim(PrimitiveType.Cube, "JambR", t, new Vector3(5.7f, 1.1f, 8.2f), new Vector3(0.3f, 2.2f, 0.35f), trim);
        Prim(PrimitiveType.Cube, "Lintel", t, new Vector3(5.0f, 2.32f, 8.2f), new Vector3(1.7f, 0.25f, 0.35f), trim);

        // Off to one side, out of the walking line.
        const float bx = -2.6f, bz = 1.4f;
        Prim(PrimitiveType.Cube, "BenchTop", t, new Vector3(bx, 0.62f, bz), new Vector3(1.4f, 0.1f, 0.42f), prop);
        for (int i = 0; i < 4; i++)
        {
            float x = bx + ((i % 2 == 0) ? -0.6f : 0.6f);
            float z = bz + ((i < 2) ? -0.14f : 0.14f);
            Prim(PrimitiveType.Cube, "BenchLeg" + i, t, new Vector3(x, 0.29f, z), new Vector3(0.09f, 0.58f, 0.09f), prop);
        }
        for (int i = 0; i < 3; i++)
        {
            var c = Prim(PrimitiveType.Cube, "Grabbable" + i, t,
                         new Vector3(bx - 0.34f + i * 0.34f, 0.76f, bz - 0.07f), Vector3.one * 0.16f, trim);
            RB(c, 0.6f);
        }
    }

    // ---------- the job ----------

    /// A table out in the street with something on it that has to come home.
    ///
    /// Height is not decoration: the hands cannot reach below about 0.74, because the
    /// shoulder sits at 1.48 and the arm's vertical span is 0.62 less a 0.12 offset.
    /// Anything to be picked up has to sit at table height. The tray is long on
    /// purpose -- one hand on one end and the other end drops and drags, so two hands
    /// is the answer the players find by looking rather than by being told.
    static CoatLoot BuildJob(CoatGame game, CoatVan van)
    {
        var prop = Mat("CoatProp", CoatPalette.Wood);
        var silver = Mat("CoatTray", CoatPalette.Tray);
        var icing = Mat("CoatCake", CoatPalette.Icing);

        var root = new GameObject("TheJob");
        var t = root.transform;
        t.position = new Vector3(2.0f, 0f, 3.0f);

        // A counter, not a low table. The shoulder is at 1.48 and the arm is 0.60,
        // so the hand cannot get below about 0.92 however it is aimed; a 0.78 tray
        // was only reachable because the grab radius happened to bridge the gap.
        const float topY = 0.90f;
        Local(PrimitiveType.Cube, "TableTop", t, new Vector3(0f, topY, 0f),
              new Vector3(1.2f, 0.08f, 0.6f), prop);
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0) ? -0.5f : 0.5f;
            float z = (i < 2) ? -0.22f : 0.22f;
            Local(PrimitiveType.Cube, "TableLeg" + i, t, new Vector3(x, topY * 0.5f, z),
                  new Vector3(0.07f, topY, 0.07f), prop);
        }

        // The tray itself. Its own object, not a child of the table, because it has to
        // be able to leave.
        var trayGo = Prim(PrimitiveType.Cube, "Tray", null,
                          t.position + new Vector3(0f, topY + 0.08f, 0f),
                          new Vector3(0.90f, 0.06f, 0.34f), silver);
        trayGo.transform.SetParent(t, true);
        var tray = RB(trayGo, 1.5f);
        tray.interpolation = RigidbodyInterpolation.Interpolate;

        // Something on it, so you can see which way up it is. Renderer only -- a second
        // body would just fall off the moment the tray tilted.
        var cake = Local(PrimitiveType.Cube, "Cake", trayGo.transform, new Vector3(0f, 0.9f, 0f),
                         new Vector3(0.42f, 0.8f, 0.7f), icing);
        Object.DestroyImmediate(cake.GetComponent<Collider>());

        var loot = root.AddComponent<CoatLoot>();
        loot.Game = game;
        loot.Van = van;
        loot.Body = tray;
        loot.Called = "the cake";
        return loot;
    }

    // ---------- the van ----------

    /// A box van with a roller shutter on the back, parked with its opening towards
    /// the level. No floor panel on purpose: the ground is the floor, so there is no
    /// lip to trip the foot solver on the way out. A fourteen centimetre kerb already
    /// pitches the disguise over; a van sill would be worse.
    static CoatVan BuildVan(CoatGame game, TheCoat coat)
    {
        var panel = Mat("CoatVanPanel", CoatPalette.Van);
        var shutter = Mat("CoatVanShutter", CoatPalette.VanShutter);
        var rubber = Mat("CoatVanTyre", CoatPalette.Tyre);

        var root = new GameObject("Van");
        var t = root.transform;
        t.position = VanSpot;
        t.rotation = Quaternion.identity;

        const float halfW = 1.2f, roofY = 2.15f, halfL = 1.65f;

        Local(PrimitiveType.Cube, "SideL", t, new Vector3(-halfW, 1.05f, 0f),
              new Vector3(0.1f, 2.1f, halfL * 2f), panel);
        Local(PrimitiveType.Cube, "SideR", t, new Vector3(halfW, 1.05f, 0f),
              new Vector3(0.1f, 2.1f, halfL * 2f), panel);
        Local(PrimitiveType.Cube, "Roof", t, new Vector3(0f, roofY, 0f),
              new Vector3(2.5f, 0.1f, halfL * 2f + 0.1f), panel);
        // The far end from the level, which is where the camera watches from. It does
        // not need hiding by name: CoatSeeThrough fades whatever is actually in the way.
        Local(PrimitiveType.Cube, "Bulkhead", t, new Vector3(0f, 1.05f, -halfL),
              new Vector3(2.5f, 2.1f, 0.1f), panel);

        // The cab, for silhouette only. Nobody gets in it.
        Local(PrimitiveType.Cube, "Cab", t, new Vector3(0f, 0.85f, -halfL - 0.75f),
              new Vector3(2.3f, 1.7f, 1.4f), panel);

        var door = Local(PrimitiveType.Cube, "Shutter", t, new Vector3(0f, 1.05f, halfL),
                         new Vector3(2.32f, 2.1f, 0.08f), shutter);

        // Wheels are decoration. Their colliders would sit in the walking line beside
        // the van, so they do not get any.
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0) ? -halfW - 0.08f : halfW + 0.08f;
            float z = (i < 2) ? 0.95f : -1.45f;
            var w = Local(PrimitiveType.Cylinder, "Wheel" + i, t, new Vector3(x, 0.34f, z),
                          new Vector3(0.68f, 0.08f, 0.68f), rubber);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Object.DestroyImmediate(w.GetComponent<Collider>());
        }

        var van = root.AddComponent<CoatVan>();
        van.Game = game;
        van.Coat = coat;
        van.Door = door.transform;
        return van;
    }

    /// Like Prim, but positioned in the parent's LOCAL space rather than the world.
    static GameObject Local(PrimitiveType type, string name, Transform parent,
                            Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // ---------- the original rig ----------

    /// One body, two legs, two arms, four players. Rebuilt with the numbers we arrived
    /// at the first time round, so the two rigs can be compared honestly.
    static Coat.Classic.ClassicRagdoll BuildClassic()
    {
        const float PelvisY = 1.00f, TorsoY = 1.32f, HeadY = 1.68f;
        const float HipX = 0.11f, HipY = 0.92f, KneeY = 0.53f, AnkleY = 0.15f;
        const float ShX = 0.20f, ShY = 1.48f, ElbowY = 1.16f, WristY = 0.88f;

        var cloth = Mat("CoatCloth", new Color(0.20f, 0.21f, 0.25f));
        var skin = Mat("CoatSkin", CoatPalette.Skin);
        var left = Mat("CoatPersonLeftLeg", new Color(0.28f, 0.55f, 0.86f));
        var right = Mat("CoatPersonRightLeg", new Color(0.88f, 0.47f, 0.28f));

        var root = new GameObject("ClassicRig");
        var t = root.transform;
        root.transform.position = Vector3.zero;

        var pelvisGo = Prim(PrimitiveType.Cube, "Pelvis", t, new Vector3(0f, PelvisY, 0f),
                            new Vector3(0.32f, 0.20f, 0.22f), cloth);
        var pelvis = RB(pelvisGo, 12f);

        var torsoGo = Prim(PrimitiveType.Cube, "Torso", t, new Vector3(0f, TorsoY, 0f),
                           new Vector3(0.38f, 0.46f, 0.24f), cloth);
        var torso = RB(torsoGo, 20f);
        Hinge(torso, pelvis, new Vector3(0f, -0.5f, 0f), 25f, 3500f, 300f);

        var head = Ball("Head", t, new Vector3(0f, HeadY, 0f), 0.24f, 5f, skin);
        Hinge(head, torso, new Vector3(0f, -0.5f, 0f), 30f, 400f, 40f);

        var legL = ClassicLeg(t, pelvis, -HipX, HipY, KneeY, AnkleY, cloth, left, CoatRole.LeftLeg);
        var legR = ClassicLeg(t, pelvis, HipX, HipY, KneeY, AnkleY, cloth, right, CoatRole.RightLeg);
        legL.Partner = legR;
        legR.Partner = legL;

        var armL = ClassicArm(t, torso, -ShX, ShY, ElbowY, WristY, cloth, left, CoatRole.LeftArm, -1f);
        var armR = ClassicArm(t, torso, ShX, ShY, ElbowY, WristY, cloth, right, CoatRole.RightArm, 1f);

        var input = root.AddComponent<LocalCoatInput>();
        var body = root.AddComponent<Coat.Classic.ClassicRagdoll>();
        body.Input = input;
        body.Pelvis = pelvis;
        body.Torso = torso;
        body.Head = head;
        body.LegL = legL;
        body.LegR = legR;
        body.ArmL = armL;
        body.ArmR = armR;

        ClassicCoat(t, body);

        // Two arrows on the floor: green for where it is being steered, orange for
        // where it actually points. Toggle with the H key.
        var arrow = root.AddComponent<Coat.Classic.ClassicHeadingArrow>();
        arrow.Body = body;

        SetLayerRecursive(root, CoatLayers.Rig);
        root.SetActive(false);   // the four-people rig is what you land in
        return body;
    }

    /// The coat draped on the single body: collar off the torso, waist on the pelvis,
    /// hem hanging free and pushed out by the legs from the inside.
    static void ClassicCoat(Transform parent, Coat.Classic.ClassicRagdoll body)
    {
        var fabric = Mat("CoatFabric", CoatPalette.Trenchcoat, CoatPalette.TrenchcoatShade);
        // A one-sided tube shows you its own interior from the wrong angle.
        if (fabric.HasProperty("_Cull")) fabric.SetFloat("_Cull", 0f);
        fabric.doubleSidedGI = true;
        EditorUtility.SetDirty(fabric);

        var go = new GameObject("Coat");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().sharedMaterial = fabric;

        var cloth = go.AddComponent<Coat.Classic.ClassicCloth>();
        cloth.Body = body;
    }

    static Coat.Classic.ClassicLimb ClassicLeg(Transform t, Rigidbody pelvis, float x,
                                               float hipY, float kneeY, float ankleY,
                                               Material cloth, Material team, CoatRole role)
    {
        string s = role == CoatRole.LeftLeg ? "L" : "R";

        var thigh = Bone("Thigh" + s, t, new Vector3(x, hipY, 0f), new Vector3(x, kneeY, 0f),
                         0.085f, 4.5f, cloth, out float upperLen);
        var shin = Bone("Shin" + s, t, new Vector3(x, kneeY, 0f), new Vector3(x, ankleY, 0f),
                        0.070f, 2.6f, team, out float lowerLen);
        var footGo = Prim(PrimitiveType.Cube, "Foot" + s, t, new Vector3(x, 0.055f, 0.06f),
                          new Vector3(0.11f, 0.09f, 0.24f), team);
        var foot = RB(footGo, 1f);

        // The knee sits around 70 degrees bent just standing, so it needs room to fold.
        var hip = Hinge(thigh, pelvis, new Vector3(0f, -1f, 0f), 100f, 6000f, 130f);
        var knee = Hinge(shin, thigh, new Vector3(0f, -1f, 0f), 140f, 4500f, 110f);
        // Wide enough for the ankle to reach level with the knee bent right up, and
        // stiff enough to hold the everyday pose on its own. A torque on the foot backs
        // it up for the moments the swinging shin would otherwise win.
        // Standing with the knees properly bent puts the shin about fifty degrees off
        // vertical, so the ankle has that much to undo before the sole is flat. It needs
        // real authority to do it while the leg IK is swinging the shin around.
        var ankle = Hinge(foot, shin, new Vector3(0f, 1f, -0.25f), 120f, 3200f, 150f);

        var limb = thigh.gameObject.AddComponent<Coat.Classic.ClassicLimb>();
        limb.Role = role;
        limb.IsLeg = true;
        limb.Side = x < 0f ? -1f : 1f;
        limb.Root = pelvis;
        limb.Upper = thigh;
        limb.Lower = shin;
        limb.End = foot;
        limb.UpperJoint = hip;
        limb.LowerJoint = knee;
        limb.EndJoint = ankle;
        limb.RootAnchorLocal = pelvis.transform.InverseTransformPoint(new Vector3(x, hipY, 0f));
        limb.UpperLen = upperLen;
        limb.LowerLen = lowerLen;
        limb.AnkleHeight = ankleY;
        limb.BendSign = -1f;   // MEASURED, not reasoned about: with +1 the knee sat 29cm BEHIND the
                              // line from hip to ankle -- a bird's leg. From the side that silhouette
                              // reads as a body facing the other way, which is how it was reported.
        return limb;
    }

    static Coat.Classic.ClassicLimb ClassicArm(Transform t, Rigidbody torso, float x,
                                               float shY, float elbowY, float wristY,
                                               Material cloth, Material team, CoatRole role, float side)
    {
        string s = role == CoatRole.LeftArm ? "L" : "R";

        var upper = Bone("UpperArm" + s, t, new Vector3(x, shY, 0f), new Vector3(x, elbowY, 0f),
                         0.055f, 2.5f, cloth, out float upperLen);
        var lower = Bone("Forearm" + s, t, new Vector3(x, elbowY, 0f), new Vector3(x, wristY, 0f),
                         0.048f, 1.8f, cloth, out float lowerLen);
        var hand = Ball("Hand" + s, t, new Vector3(x, wristY - 0.07f, 0f), 0.14f, 0.8f, team);

        var shoulder = Hinge(upper, torso, new Vector3(0f, -1f, 0f), 95f, 900f, 90f);
        var elbow = Hinge(lower, upper, new Vector3(0f, -1f, 0f), 90f, 700f, 70f);
        Hinge(hand, lower, new Vector3(0f, 0.45f, 0f), 45f, 200f, 20f);

        var limb = upper.gameObject.AddComponent<Coat.Classic.ClassicLimb>();
        limb.Role = role;
        limb.IsLeg = false;
        limb.Side = side;
        limb.Root = torso;
        limb.Upper = upper;
        limb.Lower = lower;
        limb.End = hand;
        limb.UpperJoint = shoulder;
        limb.LowerJoint = elbow;
        limb.RootAnchorLocal = torso.transform.InverseTransformPoint(new Vector3(x, shY, 0f));
        limb.UpperLen = upperLen;
        limb.LowerLen = lowerLen;
        // MEASURED, like the knees. -1 is the sign that points the middle joint
        // FORWARD -- right for a knee, wrong for an elbow, which points back and
        // out. At -1 the elbow stood 6 cm in front of the shoulder-to-wrist line
        // at rest and 14 cm with the hand raised. Arms are the opposite of legs.
        limb.BendSign = 1f;
        return limb;
    }

    static CoatObserver BuildObserver(TheCoat coat, CoatCharacter[] people)
    {
        var suit = Mat("CoatObserverSuit", CoatPalette.ObserverSuit, CoatPalette.ObserverSuitShade);
        var skin = Mat("CoatSkin", CoatPalette.Skin);

        var root = new GameObject("Observer");
        root.transform.position = new Vector3(2.6f, 0f, 4.4f);
        root.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

        var body = Prim(PrimitiveType.Capsule, "Body", root.transform,
                        new Vector3(2.6f, 0.92f, 4.4f), new Vector3(0.48f, 0.9f, 0.48f), suit);
        Prim(PrimitiveType.Sphere, "Head", root.transform,
             new Vector3(2.6f, 1.94f, 4.4f), Vector3.one * 0.26f, skin);

        var eye = new GameObject("Eye").transform;
        eye.SetParent(root.transform, false);
        eye.localPosition = new Vector3(0f, 1.94f, 0.18f);

        var obs = root.AddComponent<CoatObserver>();
        obs.Coat = coat;
        obs.Characters = people;
        obs.Eye = eye;
        obs.Skin = body.GetComponent<Renderer>();
        return obs;
    }

    /// The original passer-by, stood where they stood before: closer in and square on,
    /// because back then there was only ever one body to watch.
    static Coat.Classic.ClassicObserver BuildClassicObserver(Coat.Classic.ClassicRagdoll body)
    {
        var suit = Mat("CoatObserverSuit", CoatPalette.ObserverSuit, CoatPalette.ObserverSuitShade);
        var skin = Mat("CoatSkin", CoatPalette.Skin);

        var root = new GameObject("ClassicObserver");
        root.transform.position = new Vector3(2.2f, 0f, 3.4f);
        root.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

        var suitGo = Prim(PrimitiveType.Capsule, "Body", root.transform,
                          new Vector3(2.2f, 0.92f, 3.4f), new Vector3(0.48f, 0.9f, 0.48f), suit);
        Prim(PrimitiveType.Sphere, "Head", root.transform,
             new Vector3(2.2f, 1.94f, 3.4f), Vector3.one * 0.26f, skin);

        var eye = new GameObject("Eye").transform;
        eye.SetParent(root.transform, false);
        eye.localPosition = new Vector3(0f, 1.94f, 0.18f);

        var obs = root.AddComponent<Coat.Classic.ClassicObserver>();
        obs.Body = body;
        obs.Eye = eye;
        obs.Skin = suitGo.GetComponent<Renderer>();

        root.SetActive(false);   // shown with the rig it watches
        return obs;
    }

    static void SetupCamera(CoatGame game, TheCoat coat, CoatObserver observer, LocalCoatInput input,
                            Coat.Classic.ClassicRagdoll classic, Coat.Classic.ClassicObserver classicObserver)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }

        var follow = cam.GetComponent<CoatCamera>();
        if (follow == null) follow = cam.gameObject.AddComponent<CoatCamera>();
        follow.Game = game;
        follow.Fallback = coat.transform;
        cam.transform.position = coat.transform.position + new Vector3(0f, 3.0f, -6.5f);
        cam.transform.LookAt(coat.transform.position);

        game.CameraRef = cam.transform;

        var hud = cam.GetComponent<CoatHud>();
        if (hud == null) hud = cam.gameObject.AddComponent<CoatHud>();
        hud.Game = game;
        hud.Input = input;
        hud.Coat = coat;
        hud.Cam = follow;
        hud.Observer = observer;

        var classicHud = cam.GetComponent<Coat.Classic.ClassicHud>();
        if (classicHud == null) classicHud = cam.gameObject.AddComponent<Coat.Classic.ClassicHud>();
        classicHud.Body = classic;
        classicHud.Input = classic.Input;
        classicHud.Cam = follow;
        classicHud.Observer = classicObserver;
        classicHud.enabled = false;

        classic.CameraRef = cam.transform;

        // The handover. Lives beside the game rather than on the camera, because it is
        // game logic and the game root is never switched off.
        var vehicle = game.GetComponent<CoatVehicle>();
        if (vehicle == null) vehicle = game.gameObject.AddComponent<CoatVehicle>();
        vehicle.Game = game;
        vehicle.Coat = coat;
        vehicle.Body = classic;
        vehicle.LooseObserver = observer.gameObject;
        vehicle.WornObserver = classicObserver.gameObject;
        vehicle.LooseHud = hud;
        vehicle.WornHud = classicHud;
        vehicle.Cam = follow;

        var seeThrough = cam.GetComponent<CoatSeeThrough>();
        if (seeThrough == null) seeThrough = cam.gameObject.AddComponent<CoatSeeThrough>();
        seeThrough.Game = game;
        seeThrough.Eye = cam;

        // The van needs the vehicle to know where a player riding in the coat is.
        if (game.Van != null) game.Van.Vehicle = vehicle;
        game.Vehicle = vehicle;

        // An older build put a mode switch here; without this its corpse keeps eating Tab.
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(cam.gameObject);
    }

    // ---------- primitives ----------

    static Rigidbody Capsule(string name, Transform parent, Vector3 centre, float radius, float height,
                             float mass, Material mat)
    {
        var go = Prim(PrimitiveType.Capsule, name, parent, centre,
                      new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat);
        return RB(go, mass);
    }

    static Rigidbody Ball(string name, Transform parent, Vector3 centre, float diameter, float mass, Material mat)
    {
        var go = Prim(PrimitiveType.Sphere, name, parent, centre, Vector3.one * diameter, mat);
        return RB(go, mass);
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Rigidbody RB(GameObject go, float mass)
    {
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // A chain of loaded joints pulls apart badly at the default iteration count:
        // every link sagged, so the upper body sat crushed down into the hips.
        rb.solverIterations = 24;
        rb.solverVelocityIterations = 10;
        return rb;
    }

    /// A capsule whose local +Y runs from the proximal end to the distal end.
    /// CoatLeg's IK relies on that convention.
    static Rigidbody Bone(string name, Transform parent, Vector3 from, Vector3 to,
                          float radius, float mass, Material mat, out float length)
    {
        Vector3 dir = to - from;
        length = dir.magnitude;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = (from + to) * 0.5f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return RB(go, mass);
    }

    static ConfigurableJoint Hinge(Rigidbody child, Rigidbody parent, Vector3 anchor, float limit, float spring, float damper)
    {
        var j = child.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = parent;
        j.anchor = anchor;
        j.autoConfigureConnectedAnchor = true;

        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
        j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
        j.lowAngularXLimit = new SoftJointLimit { limit = -limit };
        j.highAngularXLimit = new SoftJointLimit { limit = limit };
        j.angularYLimit = new SoftJointLimit { limit = limit };
        j.angularZLimit = new SoftJointLimit { limit = limit };

        j.rotationDriveMode = RotationDriveMode.Slerp;
        j.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = Mathf.Infinity };
        j.enablePreprocessing = false;

        // No projection. Projection TELEPORTS the child body to satisfy the joint the
        // moment it deviates past projectionAngle -- and a knee sits ninety degrees off
        // its rest pose just standing up, so at fifteen degrees it was firing on every
        // joint every frame, shoving limbs into poses their own limits forbid. That is
        // how a leg ended up folded to a tenth of its length with the foot at the hip.
        j.projectionMode = JointProjectionMode.None;
        return j;
    }

    /// Hands are driven by script, not by joints. This is only a safety rope so a hand
    /// snagged on geometry cannot be left behind in another room.
    static void Leash(Rigidbody body, Rigidbody to, float limit)
    {
        var j = body.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = to;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = Vector3.zero;
        j.connectedAnchor = Vector3.zero;
        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
        j.linearLimit = new SoftJointLimit { limit = limit };
        j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
    }

    // ---------- misc ----------

    static Material Mat(string name, Color c, Color? shade = null)
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");

        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        // Toon materials (the characters, Coat/Toon) carry a shadow tone too.
        // An existing material keeps its shader here, so only those have one.
        if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", shade ?? CoatPalette.Shade(c));
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    /// Walks the scene roots rather than using GameObject.Find, which silently skips
    /// anything inactive: the classic rig is built switched off, so Find never saw it
    /// and every rebuild stacked another copy on top of the last.
    static void DestroyIfExists(string name)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == name) Object.DestroyImmediate(go);
    }

    static void EnsureLayer(int index, string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null || index >= layers.arraySize) return;

        var el = layers.GetArrayElementAtIndex(index);
        if (el.stringValue != name)
        {
            el.stringValue = name;
            so.ApplyModifiedProperties();
        }
    }
}
