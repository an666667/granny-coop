using UnityEngine;

namespace GrannyCoop
{
    /// <summary>
    /// Central configuration for the co-op backend.
    /// The backend is a generic game service (lobby + relay) shared by all games;
    /// this game identifies itself with GameId, exactly like the other games do.
    /// </summary>
    public static class NetConfig
    {
        // ---- backend endpoints (your own server) ----
        public const string GameId = "granny-coop";
        public const string BaseUrl = "http://186.241.123.217:8092";   // REST
        public const string WsBaseUrl = "ws://186.241.123.217:8092";   // WebSocket relay

        // ---- tuning ----
        public const float StateSendRate = 12f;   // player/enemy state messages per second
        public const float InterpSpeed = 12f;     // remote avatar interpolation speed
        public const bool Verbose = true;

        public static void Log(string s)
        {
            if (Verbose) Debug.Log("[Coop] " + s);
        }
        public static void LogError(string s)
        {
            Debug.LogError("[Coop] " + s);
        }
    }
}
