using System.Collections.Generic;
using UnityEngine;

namespace Coat
{
    /// The round reveal: a strip of cards that rips past and decelerates onto
    /// the one you are about to play.
    ///
    /// **It does not pick anything.** The round is already drawn, by
    /// CoatRounds, before this is ever shown -- the reel is handed a winner and
    /// its only job is to arrive at it. That ordering matters beyond tidiness:
    /// once the host draws and replicates the result, every client has to watch
    /// a reveal that lands on the SAME round, and a reel that rolled its own
    /// would contradict the game it is introducing. Only the cards it sweeps
    /// past on the way are decoration, and those can be whatever.
    ///
    /// A plain class rather than a MonoBehaviour: it is a timer and some IMGUI,
    /// so whoever is already drawing (CoatMenu) ticks it and draws it, and
    /// there is no component to wire up in a scene.
    public sealed class CoatRoundReel
    {
        public float SpinSeconds = 3.4f;
        public float HoldSeconds = 1.4f;

        [Tooltip("How many cards sweep past before the winner. More is a " +
                 "longer, faster spin for the same duration.")]
        public int CardsBefore = 17;

        public float CardSize = 150f;
        public float Gap = 22f;

        float Pitch => CardSize + Gap;

        /// Cards already sitting LEFT of the chooser when the spin begins.
        /// Without them the strip started with its first card centred and the
        /// whole left half of the lane bare -- it read as a list loading in,
        /// not a reel already turning.
        const int LeadIn = 6;

        float StartOffset => LeadIn * Pitch;
        float EndOffset => (LeadIn + CardsBefore) * Pitch;

        enum State { Idle, Spinning, Holding }
        State _state = State.Idle;

        readonly List<RoundDef> _strip = new List<RoundDef>();
        RoundDef _winner;
        System.Action _done;

        float _elapsed;
        float _offset, _lastOffset, _speed;

        public bool Running => _state != State.Idle;
        public bool Landed => _state == State.Holding;
        public RoundDef Winner => _winner;
        public int StripLength => _strip.Count;

        /// Whichever card is under the chooser right now.
        ///
        /// Once the spin ends this MUST be the winner -- that is the reel's
        /// entire contract, since a reveal landing one card off would show a
        /// different round from the one about to be played. Public so a test
        /// can hold it to that rather than eyeballing a screenshot.
        public RoundDef Centred
        {
            get
            {
                if (_strip.Count == 0) return null;
                int i = Mathf.Clamp(Mathf.RoundToInt(_offset / Pitch), 0, _strip.Count - 1);
                return _strip[i];
            }
        }

        /// Start the reveal. onDone fires once, after the hold.
        ///
        /// With no winner (an empty round stock) it calls onDone straight away
        /// rather than showing an empty reel, so a caller can always route
        /// through here without checking first.
        public void Show(RoundDef winner, System.Action onDone)
        {
            _winner = winner;
            _done = onDone;

            if (winner == null) { _state = State.Idle; onDone?.Invoke(); return; }

            BuildStrip();
            _elapsed = 0f;
            _offset = _lastOffset = StartOffset;
            _speed = 0f;
            _state = State.Spinning;
        }

        /// The winner sits at LeadIn + CardsBefore; everything before it is filler drawn
        /// from the stock. Filler never repeats back to back, purely so the
        /// strip does not read as a stutter while it is moving.
        void BuildStrip()
        {
            _strip.Clear();
            var all = CoatRounds.All;

            RoundDef last = null;
            for (int i = 0; i < LeadIn + CardsBefore; i++)
            {
                RoundDef pick = null;
                for (int t = 0; t < 8 && all.Count > 0; t++)
                {
                    var c = all[Random.Range(0, all.Count)];
                    if (c != last || all.Count == 1) { pick = c; break; }
                }
                pick ??= (all.Count > 0 ? all[0] : _winner);
                _strip.Add(pick);
                last = pick;
            }

            _strip.Add(_winner);

            // A few past the winner so the centre is never the end of the
            // strip and there is always something entering from the right.
            for (int i = 0; i < 6; i++)
            {
                var c = all.Count > 0 ? all[Random.Range(0, all.Count)] : _winner;
                _strip.Add(c);
            }
        }

        /// Jump to the landing. Any key or click during the spin.
        public void Skip()
        {
            if (_state == State.Spinning) _elapsed = SpinSeconds;
            else if (_state == State.Holding) Finish();
        }

        void Finish()
        {
            _state = State.Idle;
            var d = _done;
            _done = null;
            d?.Invoke();
        }

        public void Tick(float dt)
        {
            if (_state == State.Idle) return;

            _elapsed += dt;

            if (_state == State.Spinning)
            {
                float t = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, SpinSeconds));

                // Quintic ease out: leaves fast, arrives almost stopped, which
                // is what makes the last card or two readable instead of a blur
                // that simply cuts.
                float e = 1f - Mathf.Pow(1f - t, 5f);
                _offset = Mathf.Lerp(StartOffset, EndOffset, e);

                _speed = Mathf.Abs(_offset - _lastOffset) / Mathf.Max(1e-4f, dt);
                _lastOffset = _offset;

                if (t >= 1f) { _state = State.Holding; _elapsed = 0f; _speed = 0f; }
            }
            else if (_elapsed >= HoldSeconds)
            {
                Finish();
            }
        }

        // ---- drawing -----------------------------------------------------

        GUIStyle _name, _big, _sub;

        void Styles()
        {
            if (_name != null) return;

            _name = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true
            };
            _name.normal.textColor = new Color(0.16f, 0.16f, 0.20f);

            _big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            _big.normal.textColor = Color.white;

            _sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true
            };
            _sub.normal.textColor = new Color(0.66f, 0.67f, 0.74f);
        }

        static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        static void Frame(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x - t, r.y - t, r.width + t * 2f, t), c);            // top
            Fill(new Rect(r.x - t, r.yMax, r.width + t * 2f, t), c);             // bottom
            Fill(new Rect(r.x - t, r.y, t, r.height), c);                        // left
            Fill(new Rect(r.xMax, r.y, t, r.height), c);                         // right
        }

        /// A stable colour per round, so a card looks like that round every
        /// time rather than flickering a new hue each spin.
        static Color Hue(RoundDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) return new Color(0.55f, 0.56f, 0.62f);
            unchecked
            {
                uint h = 2166136261u;
                foreach (char ch in d.Id) { h ^= ch; h *= 16777619u; }
                return Color.HSVToRGB((h % 1000u) / 1000f, 0.45f, 0.80f);
            }
        }

        /// Draws in the caller's reference space, assuming its GUI.matrix is
        /// already set.
        public void Draw(Vector2 reference)
        {
            if (_state == State.Idle) return;
            Styles();

            // Oversized so it also covers the letterboxing outside the
            // reference area, which the caller's matrix does not reach. Fully
            // opaque: at 0.97 the menu title and buttons ghosted through
            // behind the cards.
            Fill(new Rect(-2000f, -2000f, reference.x + 4000f, reference.y + 4000f),
                 new Color(0.05f, 0.05f, 0.07f, 1f));

            float midX = reference.x * 0.5f;
            float midY = reference.y * 0.46f;

            GUI.Label(new Rect(0f, midY - 190f, reference.x, 30f), "YOUR ROUND", _sub);

            // The lane the cards run in, so nothing spills across the screen.
            var lane = new Rect(0f, midY - CardSize * 0.85f, reference.x, CardSize * 1.7f);
            Fill(lane, new Color(0.09f, 0.09f, 0.12f, 0.9f));

            GUI.BeginGroup(lane);
            float laneMid = lane.height * 0.5f;

            for (int i = 0; i < _strip.Count; i++)
            {
                float cx = midX + i * Pitch - _offset;
                if (cx < -CardSize || cx > reference.x + CardSize) continue;

                // Cards swell towards the middle, so the one being chosen is
                // the one your eye is already on.
                float near = 1f - Mathf.Clamp01(Mathf.Abs(cx - midX) / Pitch);
                float size = CardSize * (1f + 0.16f * near);
                var card = new Rect(cx - size * 0.5f, laneMid - size * 0.5f, size, size);

                float dim = Mathf.Lerp(0.55f, 1f, near);
                Fill(card, new Color(0.93f, 0.93f, 0.95f) * dim);

                var inner = new Rect(card.x + size * 0.12f, card.y + size * 0.12f,
                                     size * 0.76f, size * 0.62f);
                Fill(inner, Hue(_strip[i]) * dim);

                if (near > 0.25f)
                    GUI.Label(new Rect(card.x, card.yMax - size * 0.24f, size, size * 0.2f),
                              _strip[i] != null ? _strip[i].Name : "", _name);
            }

            // Motion streaks, faded in by how fast the strip is actually
            // moving -- they vanish on their own as it settles.
            float blur = Mathf.Clamp01(_speed / 3800f);
            if (blur > 0.01f)
            {
                var streak = new Color(1f, 1f, 1f, 0.10f * blur);
                for (int i = 0; i < 7; i++)
                {
                    float y = lane.height * (0.16f + 0.11f * i);
                    Fill(new Rect(0f, y, lane.width, 1.5f), streak);
                }
            }

            GUI.EndGroup();

            // The chooser, fixed in the middle. Drawn after the lane so it sits
            // over the cards passing under it.
            //
            // Padded out from the card rather than hugging it, leaving a dark
            // band between the two as in the sketch. Butted right up against
            // the card, the menu's GUI scaling left a one-pixel seam along the
            // bottom edge; a deliberate gap cannot seam.
            float fs = CardSize * 1.16f + 28f;
            var chosen = new Rect(midX - fs * 0.5f, midY - fs * 0.5f, fs, fs);
            Frame(chosen, 8f, Landed ? new Color(0.36f, 0.78f, 0.42f)
                                     : new Color(0.93f, 0.93f, 0.95f));

            if (Landed && _winner != null)
            {
                GUI.Label(new Rect(0f, midY + CardSize * 1.05f, reference.x, 58f),
                          _winner.Name, _big);
                if (_winner.Placeholder)
                    GUI.Label(new Rect(0f, midY + CardSize * 1.05f + 54f, reference.x, 28f),
                              "<color=#ffd24a>placeholder — nothing is built for it yet</color>",
                              _sub);
            }
            else
            {
                GUI.Label(new Rect(0f, reference.y - 96f, reference.x, 28f),
                          "any key to skip", _sub);
            }
        }
    }
}
