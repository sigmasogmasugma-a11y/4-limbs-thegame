using UnityEngine;
using UnityEngine.SceneManagement;

namespace Coat
{
    public enum LobbyState { Idle, Hosting, Joining, InGame, Failed }

    /// What the game scene should do about the network when it opens.
    public enum LobbyStart { None, Host, Join }

    /// The seam the menu talks to. Nothing here knows about Photon: it only
    /// records what the game scene should do when it opens (Pending), and the
    /// network runner in that scene does it. The lobby code is the Fusion
    /// session name.
    ///
    /// Online (FUSION2 defined, Fusion imported): Create opens a lobby and the
    /// game scene hosts it; Join loads the game scene and joins the session with
    /// the typed code. Hosting with nobody else there is the local game as
    /// before, with the host playing the empty limbs, and if Photon cannot be
    /// reached the scene simply plays offline.
    ///
    /// Offline (no Fusion): Create starts the local four-on-one-keyboard game,
    /// and Join refuses with a reason instead of pretending to dial out.
    public static class CoatLobby
    {
        public static LobbyState State { get; private set; } = LobbyState.Idle;
        public static string Code { get; private set; }
        public static string Message { get; private set; }

        /// True when Fusion is in. The one place that reads the build symbol;
        /// everything else reads this.
        public static bool Online =>
#if FUSION2
            true;
#else
            false;
#endif

        /// What the game scene should do with the network when it opens: host
        /// the lobby just opened, join the one whose code was typed, or nothing
        /// (offline, or Play pressed straight on the game scene). Taken once, by
        /// the network runner in that scene.
        public static LobbyStart Pending { get; private set; }

        public static LobbyStart TakePending()
        {
            var p = Pending;
            Pending = LobbyStart.None;
            return p;
        }

        /// Raised when leaving the game for the menu, BEFORE the menu loads, so
        /// an online session is closed while its scene still exists.
        public static event System.Action Leaving;

        /// Why the last online game ended under this player (the host left, the
        /// code was wrong), for the menu to say once it is back.
        static string _endedBecause;
        public static void Ended(string why) => _endedBecause = why;

        public static string TakeEndedReason()
        {
            var why = _endedBecause;
            _endedBecause = null;
            return why;
        }

        public static string GameScene = "SampleScene";

        public static void Reset()
        {
            State = LobbyState.Idle;
            Code = null;
            Message = null;
            Pending = LobbyStart.None;
        }

        /// Four letters, no vowels, so no code ever reads as a word.
        public static string NewCode()
        {
            const string alphabet = "BCDFGHJKLMNPQRSTVWXYZ0123456789";
            var c = new char[4];
            for (int i = 0; i < c.Length; i++) c[i] = alphabet[Random.Range(0, alphabet.Length)];
            return new string(c);
        }

        /// Open a lobby and draw the round, WITHOUT entering the game.
        ///
        /// Split out so the reveal has somewhere to live: the draw has to be
        /// settled before the reel can land on it, and the scene must not load
        /// until the reel has finished. Create still does both for anything
        /// that does not want a reveal.
        public static bool Open(out string error)
        {
            error = null;
            Code = NewCode();
            State = LobbyState.Hosting;

            // Whoever opens the lobby is the leader, and the leader breaks
            // ties on the coat and the head. Seat 0 holds while roles are
            // still fixed to slots -- once something DEALS roles, that is what
            // has to set this, because by then the leader is a person who
            // could be sitting in any of the four.
            CoatLoadout.Leader = CoatRole.LeftLeg;

            // The round is drawn HERE, by whoever opened the lobby, and not in
            // Join. The draw leans on a play history that belongs to one
            // player, so two peers drawing for themselves would land on two
            // different rounds however carefully the seed was shared. The host
            // draws; a joining client is told the answer.
            var round = CoatRounds.Begin(CoatSave.Current);

            Message = Online
                ? "Lobby " + Code + " open. Friends join with that code."
                : "Offline: all four on one keyboard. Online needs Photon Fusion imported.";
            if (round != null) Message = round.Name + " -- " + Message;
            return true;
        }

        /// Enter the game the lobby was opened for.
        public static bool StartGame(out string error)
        {
            Pending = Online ? LobbyStart.Host : LobbyStart.None;
            if (!LoadGame(out error)) { State = LobbyState.Failed; Pending = LobbyStart.None; return false; }
            State = LobbyState.InGame;
            return true;
        }

        public static bool Create(out string error)
            => Open(out error) && StartGame(out error);

        public static bool Join(string code, out string error)
        {
            error = null;
            code = (code ?? "").Trim().ToUpperInvariant();

            if (code.Length == 0) { error = "Enter a lobby code."; State = LobbyState.Failed; return false; }
            if (code.Length != 4) { error = "Codes are four characters."; State = LobbyState.Failed; return false; }

            if (!Online)
            {
                // Refusing is the point. Loading the local game here would look
                // like a successful join and then quietly be a different game
                // to the one the code belongs to.
                error = "Cannot reach a lobby: Photon Fusion is not imported yet, " +
                        "so there is no transport to join over. Create Lobby still " +
                        "works offline.";
                State = LobbyState.Failed;
                return false;
            }

            Code = code;
            State = LobbyState.Joining;
            Pending = LobbyStart.Join;
            if (!LoadGame(out error)) { State = LobbyState.Failed; Pending = LobbyStart.None; return false; }
            State = LobbyState.InGame;
            return true;
        }

        public static void Leave()
        {
            Leaving?.Invoke();
            Reset();
        }

        static bool LoadGame(out string error)
        {
            error = null;
            if (Application.CanStreamedLevelBeLoaded(GameScene))
            {
                SceneManager.LoadScene(GameScene);
                return true;
            }
            error = "Scene '" + GameScene + "' is not in the build settings.";
            return false;
        }
    }
}
