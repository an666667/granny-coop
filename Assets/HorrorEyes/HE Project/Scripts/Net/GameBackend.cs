using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GrannyCoop
{
    // ---- response models (mirror the backend contract) ----
    [Serializable] public class GuestData { public string playerId; public string token; public string name; public string gameId; }
    [Serializable] public class RoomData { public string roomId; public string code; public string role; public string gameId; public string wsPath; }
    [Serializable] internal class GuestEnvelope { public int code; public string msg; public GuestData data; }
    [Serializable] internal class RoomEnvelope { public int code; public string msg; public RoomData data; }
    [Serializable] internal class PlainEnvelope { public int code; public string msg; }

    /// <summary>
    /// Reserved client-side interface to the generic game backend.
    /// The game only talks through this class; the backend implementation is separate.
    /// Every other game on the platform uses this same interface style:
    ///   session/guest -> room/create|join|leave, then connect the WebSocket relay.
    /// </summary>
    public static class GameBackend
    {
        public static string Token { get; private set; }
        public static string PlayerId { get; private set; }
        public static string PlayerName { get; private set; }
        public static bool LoggedIn { get { return !string.IsNullOrEmpty(Token); } }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static IEnumerator Post(string path, string jsonBody, bool auth, Action<bool, string> done)
        {
            using (var req = new UnityWebRequest(NetConfig.BaseUrl + path, "POST"))
            {
                var raw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (auth && LoggedIn) req.SetRequestHeader("Authorization", "Bearer " + Token);
                req.timeout = 15;
                yield return req.SendWebRequest();

                bool ok = req.responseCode >= 200 && req.responseCode < 300 && !string.IsNullOrEmpty(req.downloadHandler.text);
                if (!ok)
                {
                    string err = req.error;
                    if (string.IsNullOrEmpty(err)) err = "HTTP " + req.responseCode;
                    done(false, err);
                }
                else
                {
                    done(true, req.downloadHandler.text);
                }
            }
        }

        /// <summary>Guest login. Returns token + playerId. No registration needed.</summary>
        public static IEnumerator GuestLogin(string name, Action<bool, string> done)
        {
            if (done == null) done = (a, b) => { };
            string body = "{\"gameId\":\"" + NetConfig.GameId + "\",\"name\":\"" + Escape(name) + "\"}";
            yield return Post("/api/v1/session/guest", body, false, (ok, txt) =>
            {
                if (!ok) { done(false, txt); return; }
                var env = JsonUtility.FromJson<GuestEnvelope>(txt);
                if (env == null || env.code != 0 || env.data == null) { done(false, "guest login failed"); return; }
                Token = env.data.token;
                PlayerId = env.data.playerId;
                PlayerName = env.data.name;
                NetConfig.Log("guest login ok: " + PlayerId + " (" + PlayerName + ")");
                done(true, null);
            });
        }

        /// <summary>Create a room. Caller becomes the host (authoritative player).</summary>
        public static IEnumerator CreateRoom(Action<bool, RoomData, string> done)
        {
            if (done == null) done = (a, b, c) => { };
            string body = "{\"gameId\":\"" + NetConfig.GameId + "\"}";
            yield return Post("/api/v1/room/create", body, true, (ok, txt) =>
            {
                if (!ok) { done(false, null, txt); return; }
                var env = JsonUtility.FromJson<RoomEnvelope>(txt);
                if (env == null || env.code != 0 || env.data == null) { done(false, null, "create room failed"); return; }
                NetConfig.Log("room created: " + env.data.code);
                done(true, env.data, null);
            });
        }

        /// <summary>Join an existing room by code. Caller becomes the client.</summary>
        public static IEnumerator JoinRoom(string code, Action<bool, RoomData, string> done)
        {
            if (done == null) done = (a, b, c) => { };
            string body = "{\"code\":\"" + Escape(code) + "\"}";
            yield return Post("/api/v1/room/join", body, true, (ok, txt) =>
            {
                if (!ok) { done(false, null, txt); return; }
                var env = JsonUtility.FromJson<RoomEnvelope>(txt);
                if (env == null || env.code != 0 || env.data == null) { done(false, null, "join room failed"); return; }
                NetConfig.Log("room joined: " + env.data.code);
                done(true, env.data, null);
            });
        }

        /// <summary>Leave the current room (best effort).</summary>
        public static IEnumerator LeaveRoom(string code, Action<bool> done)
        {
            string body = "{\"code\":\"" + Escape(code) + "\"}";
            yield return Post("/api/v1/room/leave", body, true, (ok, txt) => { if (done != null) done(ok); });
        }
    }
}
