using UnityEngine;
using UnityEngine.InputSystem;

namespace Coat
{
    /// The front door.
    ///
    ///     Play      Create Lobby, Join Lobby
    ///     Settings
    ///     Shop      Add, Legs, Arms, Cloak, Head
    ///     Quit
    ///
    /// IMGUI, to match CoatHud and ClassicHud and because it needs no canvas,
    /// no prefabs and no scene wiring -- the whole menu is this one component
    /// on one empty object, which is also what makes it drivable from a test.
    /// Every rule it shows lives in CoatProfile / CoatLoadout / CoatLobby, so
    /// replacing this with uGUI later does not touch any of them.
    public class CoatMenu : MonoBehaviour
    {
        enum Page { Root, Play, Settings, Shop }

        [Tooltip("Loaded by Create Lobby. Must be in the build settings.")]
        public string GameScene = "SampleScene";

        [Tooltip("Design size the layout is authored against; it scales to fit.")]
        public Vector2 Reference = new Vector2(1600f, 900f);

        Page _page = Page.Root;
        int _tab;                       // 0 is Add, then the four CosmeticSlots
        string _code = "";
        string _notice;
        Color _noticeTint = Color.white;

        CoatProfile P => CoatSave.Current;

        static readonly string[] ShopTabs = { "Add", "Legs", "Arms", "Cloak", "Head" };

        // ---- look ------------------------------------------------------

        GUIStyle _title, _sub, _btn, _tabBtn, _tabOn, _label, _small, _row, _panel;
        Texture2D _bg, _panelBg, _rowBg, _btnBg, _btnHover, _tabBg;
        Texture2D[] _bars;
        bool _built;

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        void Build()
        {
            if (_built) return;
            _built = true;

            // Frog Sqwad's menu colours (CoatPalette): plum panels, violet buttons.
            _bg       = Solid(CoatPalette.UiBackground);
            _panelBg  = Solid(WithAlpha(CoatPalette.UiPanel, 0.96f));
            _rowBg    = Solid(WithAlpha(CoatPalette.UiRow, 0.95f));
            _btnBg    = Solid(CoatPalette.UiButton);
            _btnHover = Solid(CoatPalette.UiButtonHover);
            _tabBg    = Solid(CoatPalette.UiRow);

            // Built once, not per frame: OnGUI runs several times a frame and
            // a texture per bar per pass leaks until the next collection.
            _bars = new Texture2D[4];
            for (int i = 0; i < 4; i++) _bars[i] = Solid(CoatFit.Colour((CoatRole)i));

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 62,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _title.normal.textColor = Color.white;

            _sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = true
            };
            _sub.normal.textColor = CoatPalette.UiMutedText;

            _btn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 21,
                alignment = TextAnchor.MiddleCenter
            };
            _btn.normal.background = _btnBg;
            _btn.hover.background = _btnHover;
            _btn.active.background = _btnHover;
            _btn.normal.textColor = Color.white;
            _btn.hover.textColor = Color.white;
            _btn.border = new RectOffset(2, 2, 2, 2);

            _tabBtn = new GUIStyle(_btn) { fontSize = 17 };
            _tabBtn.normal.background = _tabBg;

            _tabOn = new GUIStyle(_tabBtn) { fontStyle = FontStyle.Bold };
            _tabOn.normal.background = _btnBg;
            _tabOn.normal.textColor = Color.white;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            _label.normal.textColor = CoatPalette.UiText;

            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, richText = true, wordWrap = true
            };
            _small.normal.textColor = CoatPalette.UiMutedText;

            _row = new GUIStyle(GUI.skin.box);
            _row.normal.background = _rowBg;
            _row.border = new RectOffset(2, 2, 2, 2);

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = _panelBg;
            _panel.border = new RectOffset(2, 2, 2, 2);
            _panel.padding = new RectOffset(26, 26, 22, 22);
        }

        // ---- frame -----------------------------------------------------

        readonly CoatRoundReel _reel = new CoatRoundReel();
        public CoatRoundReel Reel => _reel;

        void Update()
        {
            var kb = Keyboard.current;

            // While the reveal is up it owns the screen: any key skips it, and
            // Escape must not sneak the menu out from underneath it.
            if (_reel.Running)
            {
                _reel.Tick(Time.deltaTime);
                bool any = (kb != null && kb.anyKey.wasPressedThisFrame) ||
                           (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (any) _reel.Skip();
                return;
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
        }

        void Back()
        {
            _notice = null;
            if (_page == Page.Root) return;
            _page = Page.Root;
        }

        /// Jump straight to a page. A harness cannot click an IMGUI button, so
        /// without this the only reachable page in a test is the root one.
        ///
        /// The change is DEFERRED to the next Layout event rather than applied
        /// here. IMGUI lays out and repaints as two separate passes over the
        /// same frame, and a caller outside OnGUI can land between them -- so
        /// setting _page directly laid out one page and repainted another, and
        /// the mismatched control count took the whole GUI down with a
        /// NullReferenceException that then repeated every frame.
        public void Open(string page, int tab = -1)
        {
            if (System.Enum.TryParse(page, true, out Page p)) _want = p;
            _wantTab = tab;
        }

        Page? _want;
        int _wantTab = -1;

        /// Open a lobby, reveal the round, then enter the game.
        ///
        /// Draw first, reveal second, load third. The reel is handed the round
        /// that is already decided -- it never picks one of its own.
        public void CreateLobby()
        {
            CoatLobby.GameScene = GameScene;
            if (!CoatLobby.Open(out string err)) { Say(err, true); return; }

            Say(CoatLobby.Message);
            _reel.Show(CoatRounds.Current, () =>
            {
                if (!CoatLobby.StartGame(out string e2)) Say(e2, true);
            });
        }

        /// Play the reveal on its own, without opening a lobby. For looking at
        /// it, and for a harness that cannot click a button.
        public void PreviewReel()
        {
            _reel.Show(CoatRounds.Pick(null, Random.Range(0, 999999)), null);
        }

        void OnGUI()
        {
            Build();

            // Layout only, so both passes of a frame agree on what is on screen.
            if (Event.current.type == EventType.Layout && _want.HasValue)
            {
                _page = _want.Value;
                if (_wantTab >= 0 && _wantTab < ShopTabs.Length) _tab = _wantTab;
                _want = null;
                _wantTab = -1;
                _notice = null;
            }

            int sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _bg);

            // One design size scaled to whatever the window is, so nothing has
            // to be laid out twice.
            float s = Mathf.Min(sw / Reference.x, sh / Reference.y);
            Matrix4x4 was = GUI.matrix;
            var pad = new Vector2((sw - Reference.x * s) * 0.5f, (sh - Reference.y * s) * 0.5f);
            GUI.matrix = Matrix4x4.TRS(pad, Quaternion.identity, new Vector3(s, s, 1f));

            switch (_page)
            {
                case Page.Root:     Root();     break;
                case Page.Play:     Play();     break;
                case Page.Settings: Options();  break;
                case Page.Shop:     Shop();     break;
            }

            // Over the top of whatever page is underneath.
            _reel.Draw(Reference);

            GUI.matrix = was;
        }

        void Header(string caption, string under = null)
        {
            GUI.Label(new Rect(0, 74, Reference.x, 78), caption, _title);

            // Four bars in the limb colours: the only branding the game has,
            // and a reminder of who is who.
            const float w = 86f, gap = 10f;
            float total = w * 4f + gap * 3f;
            float x = (Reference.x - total) * 0.5f;
            for (int i = 0; i < 4; i++)
                GUI.DrawTexture(new Rect(x + i * (w + gap), 160, w, 6), _bars[i]);

            if (!string.IsNullOrEmpty(under))
                GUI.Label(new Rect(Reference.x * 0.5f - 400, 178, 800, 44), under, _sub);
        }

        bool Btn(Rect r, string label, bool enabled = true)
        {
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            bool hit = GUI.Button(r, label, _btn);
            GUI.enabled = was;
            return hit;
        }

        Rect Column(int i, float top = 264f, float h = 56f, float gapY = 14f, float w = 360f)
        {
            float x = (Reference.x - w) * 0.5f;
            return new Rect(x, top + i * (h + gapY), w, h);
        }

        void Notice()
        {
            if (string.IsNullOrEmpty(_notice)) return;
            var was = _sub.normal.textColor;
            _sub.normal.textColor = _noticeTint;
            GUI.Label(new Rect(Reference.x * 0.5f - 420, Reference.y - 104, 840, 62), _notice, _sub);
            _sub.normal.textColor = was;
        }

        /// A checkbox and its caption on one clickable row.
        bool Check(bool on, string caption)
        {
            Rect r = GUILayoutUtility.GetRect(new GUIContent(caption), _label,
                                              GUILayout.Height(32f));
            var box = new Rect(r.x, r.y + 6f, 20f, 20f);

            // Tinted whiteTexture rather than three 1x1 textures of my own.
            // Those came back null here every frame while the title bars, made
            // the same way a few lines earlier, survived -- so the box simply
            // never drew. A built-in texture cannot go null under me.
            Color wasColour = GUI.color;
            GUI.color = CoatPalette.UiMutedText;
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = on ? CoatPalette.UiSelected : CoatPalette.UiBackground;
            GUI.DrawTexture(new Rect(box.x + 2f, box.y + 2f, box.width - 4f, box.height - 4f),
                            Texture2D.whiteTexture);
            GUI.color = wasColour;
            GUI.Label(new Rect(r.x + 32f, r.y, r.width - 32f, r.height), caption, _label);

            // The whole row is the hit target, drawn as nothing over the top.
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) on = !on;
            return on;
        }

        /// Back from an online game that ended under us (the host left, no lobby
        /// with that code): say why, on the page the menu opens on.
        void Start()
        {
            string why = CoatLobby.TakeEndedReason();
            if (!string.IsNullOrEmpty(why)) Say(why, true);
        }

        void Say(string msg, bool bad = false)
        {
            _notice = msg;
            _noticeTint = bad ? new Color(1f, 0.45f, 0.42f) : new Color(0.55f, 0.88f, 0.55f);
        }

        // ---- pages -----------------------------------------------------

        void Root()
        {
            Header("4 LIMBS", "Four players. One body. One coat.");

            // Lower than the other pages: four buttons under a title left the
            // bottom third of the screen empty and the whole thing top-heavy.
            const float top = 336f;
            if (Btn(Column(0, top), "Play"))     { _page = Page.Play; _notice = null; }
            if (Btn(Column(1, top), "Settings")) { _page = Page.Settings; _notice = null; }
            if (Btn(Column(2, top), "Shop"))     { _page = Page.Shop; _notice = null; }
            if (Btn(Column(3, top), "Quit"))     Quit();

            Notice();
        }

        void Play()
        {
            Header("PLAY");

            // Wider gaps than the other pages: the "Lobby code" caption sits in
            // the gap above its field, and at the default 14 it landed on top
            // of the Create Lobby button.
            const float top = 276f, gapY = 32f;

            if (Btn(Column(0, top, 56f, gapY), "Create Lobby")) CreateLobby();

            Rect field = Column(1, top, 56f, gapY);
            GUI.Label(new Rect(field.x, field.y - 24, field.width, 22), "Lobby code", _small);
            string typed = GUI.TextField(field, _code, 4, _btn);
            _code = typed.ToUpperInvariant();

            if (Btn(Column(2, top, 56f, gapY), "Join Lobby"))
            {
                CoatLobby.GameScene = GameScene;
                if (CoatLobby.Join(_code, out string err)) Say("Joining " + _code + "...");
                else Say(err, true);
            }

            if (Btn(Column(3, top, 56f, gapY), "Back")) Back();

            GUI.Label(new Rect(Reference.x * 0.5f - 420, Reference.y - 166, 840, 50),
                      CoatLobby.Online
                        ? "<b>Online.</b> Create Lobby hosts: your friends type its code here and " +
                          "Join. Limbs nobody has joined for are played from your keyboard."
                        : "<b>Offline build.</b> Create Lobby starts the local game, all four " +
                          "on one keyboard. Joining needs Photon Fusion imported.", _sub);
            Notice();
        }

        void Options()
        {
            Header("SETTINGS");

            var s = P.Settings;
            float w = 560f, x = (Reference.x - w) * 0.5f, y = 244f;
            bool changed = false;

            GUILayout.BeginArea(new Rect(x, y, w, 392), _panel);

            GUILayout.Label("Volume", _label);
            float vol = GUILayout.HorizontalSlider(s.Volume, 0f, 1f);
            if (!Mathf.Approximately(vol, s.Volume)) { s.Volume = vol; changed = true; }
            GUILayout.Label(Mathf.RoundToInt(s.Volume * 100f) + "%", _small);
            GUILayout.Space(10);

            GUILayout.Label("Camera speed", _label);
            float cam = GUILayout.HorizontalSlider(s.CameraSpeed, 20f, 260f);
            if (!Mathf.Approximately(cam, s.CameraSpeed)) { s.CameraSpeed = cam; changed = true; }
            GUILayout.Label(Mathf.RoundToInt(s.CameraSpeed) + " deg/s", _small);
            GUILayout.Space(10);

            bool inv = Check(s.InvertCamera, "Invert camera");
            if (inv != s.InvertCamera) { s.InvertCamera = inv; changed = true; }

            bool full = Check(s.Fullscreen, "Fullscreen");
            if (full != s.Fullscreen) { s.Fullscreen = full; changed = true; }

            bool vs = Check(s.VSync, "VSync");
            if (vs != s.VSync) { s.VSync = vs; changed = true; }

            bool hud = Check(s.ShowHud, "Show the prototype readout");
            if (hud != s.ShowHud) { s.ShowHud = hud; changed = true; }

            GUILayout.EndArea();

            if (changed) { s.Apply(); CoatSave.Save(); }

            if (Btn(new Rect(x, y + 410, 268, 52), "Reset to defaults"))
            {
                P.Settings = new CoatSettings();
                P.Settings.Apply();
                CoatSave.Save();
                Say("Settings reset.");
            }
            if (Btn(new Rect(x + 292, y + 410, 268, 52), "Back")) Back();

            Notice();
        }

        void Shop()
        {
            Header("SHOP");

            const float tw = 150f, tgap = 8f;
            float total = tw * ShopTabs.Length + tgap * (ShopTabs.Length - 1);
            float tx = (Reference.x - total) * 0.5f;
            for (int i = 0; i < ShopTabs.Length; i++)
            {
                var r = new Rect(tx + i * (tw + tgap), 232, tw, 46);
                if (GUI.Button(r, ShopTabs[i], i == _tab ? _tabOn : _tabBtn))
                {
                    _tab = i;
                    _notice = null;
                }
            }

            float w = 900f, x = (Reference.x - w) * 0.5f;
            GUI.Label(new Rect(x, 292, w, 26), "<b>" + P.Coins + "</b> coins", _label);

            if (_tab == 0) AddTab(x, w);
            else SlotTab(x, w, (CosmeticSlot)(_tab - 1));

            if (Btn(new Rect(x, Reference.y - 148, 220, 52), "Back")) Back();
            Notice();
        }

        void AddTab(float x, float w)
        {
            GUILayout.BeginArea(new Rect(x, 328, w, 300), _panel);
            GUILayout.Label("Top up", _label);
            GUILayout.Space(6);
            GUILayout.Label("Coin packs go here. Nothing is on sale yet and no payment path " +
                            "is wired up -- this tab exists so the shop has somewhere to " +
                            "put one.", _small);
            GUILayout.Space(14);
#if UNITY_EDITOR
            GUILayout.Label("Editor only:", _small);
            if (GUILayout.Button("Grant 500 coins", _btn, GUILayout.Width(220), GUILayout.Height(44)))
            {
                P.Coins += 500;
                CoatSave.Save();
                Say("500 coins added. Editor only.");
            }
#endif
            GUILayout.EndArea();
        }

        void SlotTab(float x, float w, CosmeticSlot slot)
        {
            var items = CoatCatalogue.InSlot(slot);

            GUILayout.BeginArea(new Rect(x, 328, w, 394), _panel);

            // The rule is not guessable from the UI and a player is about to
            // spend on it, so it says so before they do.
            if (CosmeticSlots.IsLimb(slot))
            {
                string mine  = slot == CosmeticSlot.Legs ? "leg" : "arm";
                string other = slot == CosmeticSlot.Legs ? "arm" : "leg";
                GUILayout.Label("Worn on whichever " + mine + " you draw, left or right. " +
                                "Draw an " + other + " instead and you wear nothing -- it " +
                                "does not move across, and it does not go to anyone else.",
                                _small);
            }
            else
            {
                GUILayout.Label("One between the four of you. Whichever is picked by the " +
                                "most of you goes on the body -- and if that is tied, the " +
                                "lobby leader's pick takes it.", _small);
            }
            GUILayout.Space(12);

            if (items.Count == 0)
            {
                GUILayout.Label("Nothing in stock yet.", _label);
                GUILayout.EndArea();
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i];
                GUILayout.BeginHorizontal(_row, GUILayout.Height(54));
                GUILayout.Space(10);
                GUILayout.Label(d.Name, _label, GUILayout.Width(360));
                GUILayout.FlexibleSpace();

                bool owns = P.Owns(d.Id);
                bool on = P.IsEquipped(d.Id);

                if (!owns)
                {
                    GUILayout.Label(d.Price + " coins", _small, GUILayout.Width(110));
                    if (GUILayout.Button("Buy", _btn, GUILayout.Width(120), GUILayout.Height(42)))
                    {
                        if (P.Buy(d, out string err)) { CoatSave.Save(); Say("Bought " + d.Name + "."); }
                        else Say(err, true);
                    }
                }
                else if (on)
                {
                    if (GUILayout.Button("Equipped", _btn, GUILayout.Width(120), GUILayout.Height(42)))
                    {
                        P.Unequip(slot);
                        CoatSave.Save();
                        Say(d.Name + " taken off.");
                    }
                }
                else
                {
                    if (GUILayout.Button("Equip", _btn, GUILayout.Width(120), GUILayout.Height(42)))
                    {
                        if (P.Equip(d.Id, out string err)) { CoatSave.Save(); Say(d.Name + " equipped."); }
                        else Say(err, true);
                    }
                }
                GUILayout.Space(10);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }

            GUILayout.EndArea();
        }

        void Quit()
        {
            CoatSave.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
