using System;

namespace GrannyCoop
{
    /// <summary>
    /// Flat network message used by both peers through the relay.
    /// Serialized with JsonUtility, so every field must be public and serializable.
    /// Only the fields relevant to a given "t" are filled.
    /// </summary>
    [Serializable]
    public class NetMsg
    {
        // message type: sys | hello | player | enemy | obj | ev | ping
        public string t;

        // ---- identity (hello) ----
        public string pid;
        public string name;
        public int avatar;      // 0 = male, 1 = female (which model the peer should render)

        // ---- player state (player) ----
        public float px, py, pz, ry;
        public float spd;       // 0..1 locomotion amount for the avatar animator
        public int crouch;
        public int run;
        public int alive;

        // ---- enemy state (enemy) ----
        public float ex, ey, ez, ery;
        public float espd;
        public int eact;        // enemy "ActionId" (0 = normal locomotion)
        public int ekill;       // enemy "KillPlayer" flag (0/1)

        // ---- objective (obj) ----
        public int pics;
        public int pills;

        // ---- one-shot events (ev) ----
        public string k;        // event key: item | door | win | lose | tip | start
        public int i;           // generic int payload (item id / tip type / door state)
        public string s;        // generic string payload (e.g. scene name)
        public int enemyMode;   // for "start": enemy mode id
        public int difficulty;  // for "start": difficulty id

        // ---- system (sys) ----
        public string e;        // joined | ready | peer_left | error
        public string you;      // host | client
        public string msg;
    }
}
