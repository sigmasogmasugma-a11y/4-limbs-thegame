using UnityEngine;
using UnityEngine.SceneManagement;

namespace Coat
{
    public enum LobbyState { Idle, Hosting, Joining, InGame, Failed }

    /// The seam the menu talks to, so Create and Join are already routed
    /// somewhere and swapping in Fusion later replaces the guts of one class
    /// rather than rewriting the front end.
    ///
    /// It is NOT online today. Photon Fusion is not imported -- it ships as a
    /// .unitypackage from the Photon dashboard against an App ID -- so the
    /// honest behaviour is: hosting starts the local four-on-one-keyboard game
    /// that already exists, and joining somebody else's code refuses with a
    /// reason instead of pretending to dial out.
    ///
    /// The agreed shape for when it lands (host-authoritative physics, input
    /// replication, one peer simulating the body) is unaffected by any of this;
    /// Create becomes StartGame(GameMode.Host) and Join becomes
    /// StartGame(GameMode.Client) with the code as the session name.
    public static class CoatLobby
    {
        public static LobbyState State { get; private set; } = LobbyState.Idle;
        public static string Code { get; private set; }
        public static string Message { get; private set; }

        /// True once a real transport is in. Everything reading this should
        /// degrade rather than branch on a build symbol.
        public static bool Online => false;

        public static string GameScene = "SampleScene";

        public static void Reset()
        {
            State = LobbyState.Idle;
            Code = null;
            Message = null;
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
                ? "Lobby " + Code + " open."
                : "Offline: all four on one keyboard. Online needs Photon Fusion imported.";
            if (round != null) Message = round.Name + " -- " + Message;
            return true;
        }

        /// Enter the game the lobby was opened for.
        public static bool StartGame(out string error)
        {
            if (!LoadGame(out error)) { State = LobbyState.Failed; return false; }
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
            if (!LoadGame(out error)) { State = LobbyState.Failed; return false; }
            State = LobbyState.InGame;
            return true;
        }

        public static void Leave()
        {
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
