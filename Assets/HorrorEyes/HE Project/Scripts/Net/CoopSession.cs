using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrannyCoop
{
    public enum CoopRole { None, Host, Client }

    /// <summary>
    /// Orchestrates the 2-player co-op session.
    ///   - host is authoritative for the world (enemy AI, objective, win/lose)
    ///   - both peers stream their own player state through the relay
    ///   - the client mirrors the host's enemies + objective
    ///   - players are solid: each side renders the peer with a body collider, so they block each other
    ///   - one death does not end the match: the dead player spectates, the game ends only when both are down
    /// Solo play is untouched: everything here is gated behind IsCoop.
    /// </summary>
    public class CoopSession : MonoBehaviour
    {
        public static CoopSession Instance { get; private set; }
        public static bool IsCoop { get { return Instance != null && Instance.Role != CoopRole.None; } }
        public static bool IsClient { get { return IsCoop && Instance.Role == CoopRole.Client; } }
        public static bool IsHost { get { return IsCoop && Instance.Role == CoopRole.Host; } }

        public static bool ForceLocalGameOver = false;

        public CoopRole Role = CoopRole.None;
        public string RoomCode = "";
        public string Status = "";
        public string PeerName = "";
        public int PeerAvatar = 0;
        public bool PeerReady = false;
        public bool IsApplyingRemote = false;

        WsClient _ws;
        string _wsUrl;
        float _wsRetryAt;

        // Host: keep re-sending "start" until the peer confirms it loaded.
        // The doc requires control messages (hello/welcome/ready/start) to be
        // reliable with a timeout retry - a single fire-and-forget send means a
        // client that was still connecting never enters the level.
        bool _awaitingPeerLoaded;
        string _startScene;
        int _startEnemy, _startDiff;
        float _startResendAt;
        int _startResendCount;

        // Client: ignore a duplicate "start" for a scene we are already loading.
        string _lastStartScene;
        float _lastStartAt;
        float _sendTimer;
        CoopAvatar _avatar;

        PlayerController _player;
        GameControll _gc;
        List<Enemy> _enemies = new List<Enemy>();

        Transform _clientProxy;
        bool _localDead;
        bool _peerDead;

        Vector3 _lastLocalPos;
        float _localSpeed;
        Vector3 _lastPeerPos;
        bool _hasPeerPos;

        // ------------------------------------------------------------ deterministic ids
        public int StableSeed()
        {
            int h = 17;
            if (!string.IsNullOrEmpty(RoomCode))
                foreach (char c in RoomCode) h = h * 31 + c;
            return h;
        }

        public static string PathId(Transform t)
        {
            if (t == null) return "";
            var sb = new System.Text.StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        // ------------------------------------------------------------ lifecycle
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        /// <summary>
        /// A backgrounded phone freezes the app and the socket is dead on return,
        /// so drop it on pause and let the reconnect path rebuild it on resume.
        /// The server keeps the room for 3 minutes, so the peer is still there.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (!IsCoop) return;
            if (paused)
            {
                NetConfig.Log("app paused -> dropping relay");
                if (_ws != null) _ws.Close();
                Status = "已切到后台";
            }
            else
            {
                NetConfig.Log("app resumed -> forcing relay reconnect");
                _wsRetryAt = 0f;
                Status = "连接断开，重连中…";
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ start flows
        public IEnumerator StartHost(string playerName, Action<bool, string> done)
        {
            Status = "登录中…";
            yield return GameBackend.GuestLogin(playerName, null);
            if (!GameBackend.LoggedIn) { done(false, "登录失败"); yield break; }

            Status = "创建房间…";
            RoomData room = null;
            yield return GameBackend.CreateRoom((ok, r, err) => { if (ok) room = r; else Status = "创建失败: " + err; });
            if (room == null) { done(false, Status); yield break; }

            Role = CoopRole.Host;
            RoomCode = room.code;
            ConnectRelay(room.code, "host");
            Status = "房间码 " + room.code + "，等待好友加入…";
            done(true, null);
        }

        public IEnumerator StartClient(string code, string playerName, Action<bool, string> done)
        {
            Status = "登录中…";
            yield return GameBackend.GuestLogin(playerName, null);
            if (!GameBackend.LoggedIn) { done(false, "登录失败"); yield break; }

            Status = "加入房间…";
            RoomData room = null;
            yield return GameBackend.JoinRoom(code, (ok, r, err) => { if (ok) room = r; else Status = "加入失败: " + err; });
            if (room == null) { done(false, Status); yield break; }

            Role = CoopRole.Client;
            RoomCode = room.code;
            ConnectRelay(room.code, "client");
            Status = "已加入房间 " + room.code + "，等待房主…";
            done(true, null);
        }

        void ConnectRelay(string code, string role)
        {
            _ws = new WsClient();
            _wsUrl = NetConfig.WsBaseUrl + "/api/v1/ws?code=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(code)
                   + "&token=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(GameBackend.Token)
                   + "&role=" + role;
            _ws.Connect(_wsUrl);
            _wsRetryAt = Time.unscaledTime + 4f;
        }

        // ---- diagnostics accessors (read by CoopHud) ----
        public CoopAvatar PeerAvatarObject { get { return _avatar; } }

        public string RoleName
        {
            get
            {
                if (Role == CoopRole.Host) return "房主";
                if (Role == CoopRole.Client) return "玩家";
                return "单机";
            }
        }

        /// <summary>Enemy state for the on-screen diagnostics.</summary>
        public string EnemyDiag()
        {
            if (_enemies.Count == 0) return "老奶奶: 未采集到";
            var e = _enemies[0];
            if (e == null) return "老奶奶: 空引用";
            Vector3 ep = e.transform.position;
            return "老奶奶 " + _enemies.Count + "个 " + (e.enabled ? "AI开" : "AI关")
                 + (e.gameObject.activeInHierarchy ? " 活跃" : " 未激活")
                 + " (" + ep.x.ToString("F1") + "," + ep.y.ToString("F1") + "," + ep.z.ToString("F1") + ")";
        }
        public bool HasLocalPlayer { get { return _player != null; } }
        public Vector3 LocalPlayerPos { get { return _player != null ? _player.transform.position : Vector3.zero; } }

        public void StopCoop()
        {
            if (_ws != null) { _ws.Close(); _ws = null; }
            if (!string.IsNullOrEmpty(RoomCode)) StartCoroutine(GameBackend.LeaveRoom(RoomCode, null));
            if (_avatar != null) { Destroy(_avatar.gameObject); _avatar = null; }
            Role = CoopRole.None;
            PeerReady = false;
            Status = "";
        }

        // ------------------------------------------------------------ scene binding
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsCoop) return;
            StartCoroutine(BindScene());
        }

        IEnumerator BindScene()
        {
            yield return null;
            yield return null;

            _player = FindObjectOfType<PlayerController>();
            _gc = FindObjectOfType<GameControll>();
            _enemies.Clear();
            _enemies.AddRange(FindObjectsOfType<Enemy>());

            _localDead = false;
            _peerDead = false;
            _hasPeerPos = false;
            if (_player != null) _lastLocalPos = _player.transform.position;

            // client does not simulate the enemy; it mirrors the host.
            // disable the body colliders too, otherwise the mirrored enemy would
            // block the player and trip automatic doors / reaction triggers.
            if (IsClient)
            {
                foreach (var e in _enemies)
                {
                    e.enabled = false;
                    foreach (var c in e.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    var rb = e.GetComponent<Rigidbody>();
                    if (rb != null) rb.isKinematic = true;
                }
            }

            // FindObjectsOfType only sees ACTIVE objects. If the scene binds
            // before the enemy spawns we would sync nothing and the peer's
            // mirror would sit frozen at the spawn point forever, so keep
            // re-collecting for a few seconds.
            if (_enemies.Count == 0)
            {
                for (int i = 0; i < 10 && _enemies.Count == 0; i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    _enemies.AddRange(FindObjectsOfType<Enemy>());
                }
                NetConfig.Log("scene bound: late enemy pickup, enemies=" + _enemies.Count);
                if (_enemies.Count > 0 && IsClient)
                {
                    foreach (var e in _enemies)
                    {
                        e.enabled = false;
                        foreach (var c in e.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                        var rb2 = e.GetComponent<Rigidbody>();
                        if (rb2 != null) rb2.isKinematic = true;
                    }
                }
            }

            _clientProxy = null;
            if (IsHost)
            {
                var proxy = GameObject.Find("CoopClientProxy");
                if (proxy == null) proxy = new GameObject("CoopClientProxy");
                proxy.layer = 0; // Default: the enemy sees it and the player collides with it
                var col = proxy.GetComponent<CapsuleCollider>();
                if (col == null) col = proxy.AddComponent<CapsuleCollider>();
                col.height = 1.8f; col.radius = 0.35f; col.center = new Vector3(0f, 0.9f, 0f);
                var rb = proxy.GetComponent<Rigidbody>();
                if (rb == null) rb = proxy.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
                proxy.SetActive(false);
                _clientProxy = proxy.transform;
                foreach (var e in _enemies) e.coopClientTarget = _clientProxy;
            }

            Transform tps = (_player != null && _player.m_TPS_Player != null) ? _player.m_TPS_Player.transform : null;
            if (_avatar != null) Destroy(_avatar.gameObject);
            _avatar = CoopAvatar.Create(PeerAvatar, tps);
            _avatar.Show(PeerReady);

            // tell the host we are in the level (doc 4.5: everyone reports loaded)
            if (IsClient) SendEv("loaded");

            NetConfig.Log("scene bound: enemies=" + _enemies.Count + " player=" + (_player != null) + " role=" + Role);
        }

        // ------------------------------------------------------------ update
        /// <summary>
        /// The relay socket can drop (app backgrounded, NAT timeout, network
        /// blip). The room survives on the server now, so just reconnect and
        /// let the server re-pair the two members by playerId.
        /// </summary>
        void TryReconnectRelay()
        {
            if (_ws == null || string.IsNullOrEmpty(_wsUrl)) return;
            if (_ws.Connected) return;
            if (Time.unscaledTime < _wsRetryAt) return;
            _wsRetryAt = Time.unscaledTime + 4f;
            Status = "连接断开，重连中…";
            NetConfig.Log("relay reconnect -> " + _wsUrl);
            _ws.Connect(_wsUrl);
        }

        void Update()
        {
            if (!IsCoop) return;

            TryReconnectRelay();
            TickStartRetry();

            if (_ws != null && _ws.Connected)
            {
                string raw;
                while (_ws.TryDequeue(out raw))
                {
                    NetMsg msg = null;
                    try { msg = JsonUtility.FromJson<NetMsg>(raw); } catch { }
                    if (msg != null) Handle(msg);
                }

                _sendTimer += Time.deltaTime;
                if (_sendTimer >= 1f / NetConfig.StateSendRate)
                {
                    _sendTimer = 0f;
                    SendLocalState();
                }
            }

            if (_localDead && !_peerDead) SpectateUpdate();
        }

        void SendLocalState()
        {
            if (_ws == null) return;

            // a dead player stops reporting position (they are spectating)
            if (!_localDead && _player != null)
            {
                var p = _player.transform.position;
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                float inst = (p - _lastLocalPos).magnitude / dt;
                _localSpeed = Mathf.Lerp(_localSpeed, Mathf.Clamp01(inst / 4.5f), 0.4f);
                _lastLocalPos = p;

                var m = new NetMsg
                {
                    t = "player",
                    px = p.x, py = p.y, pz = p.z,
                    ry = _player.transform.eulerAngles.y,
                    spd = _localSpeed,
                    crouch = _player.crouch ? 1 : 0,
                    run = _player.m_running ? 1 : 0,
                    alive = 1
                };
                _ws.Send(JsonUtility.ToJson(m));
            }

            if (IsHost)
            {
                for (int i = 0; i < _enemies.Count; i++)
                {
                    var e = _enemies[i];
                    if (e == null) continue;
                    float espd = 0f;
                    if (e.agent != null) espd = Mathf.Clamp01(e.agent.velocity.magnitude / Mathf.Max(e.walkSpeed, 0.1f));
                    var em = new NetMsg
                    {
                        t = "enemy", i = i,
                        ex = e.transform.position.x, ey = e.transform.position.y, ez = e.transform.position.z,
                        ery = e.transform.eulerAngles.y, espd = espd,
                        eact = (e.animator != null) ? e.animator.GetInteger("ActionId") : 0,
                        ekill = (e.animator != null && e.animator.GetBool("KillPlayer")) ? 1 : 0
                    };
                    _ws.Send(JsonUtility.ToJson(em));
                }
            }
        }

        void Handle(NetMsg m)
        {
            switch (m.t)
            {
                case "sys":
                    if (m.e == "ready")
                    {
                        PeerReady = true; Status = "已连接";
                        if (_avatar != null) _avatar.Show(true);
                        SendHello();
                        if (IsHost) ResendStartIfInGame();
                    }
                    else if (m.e == "peer_left")
                    {
                        PeerReady = false;
                        Status = "对方已离开";
                        if (_avatar != null) _avatar.Show(false);
                        if (_gc != null && !string.IsNullOrEmpty(_gc.m_mainMenuSceneName))
                            SceneManager.LoadScene(_gc.m_mainMenuSceneName);
                    }
                    else if (m.e == "joined")
                    {
                        Status = (Role == CoopRole.Host) ? "等待好友加入…" : "等待房主开始…";
                    }
                    else if (m.e == "error") { Status = "错误: " + m.msg; }
                    break;

                case "hello":
                    PeerName = m.name;
                    PeerAvatar = m.avatar;
                    if (_avatar != null) { Destroy(_avatar.gameObject); _avatar = null; }
                    Transform tps = (_player != null && _player.m_TPS_Player != null) ? _player.m_TPS_Player.transform : null;
                    _avatar = CoopAvatar.Create(PeerAvatar, tps);
                    _avatar.Show(PeerReady);
                    break;

                case "player":
                    _lastPeerPos = new Vector3(m.px, m.py, m.pz);
                    _hasPeerPos = true;
                    if (_avatar != null) _avatar.ApplyState(_lastPeerPos, m.ry, m.spd, m.crouch != 0);
                    if (IsHost && _clientProxy != null)
                    {
                        // the reported position is the capsule centre, so drop it to the floor
                        Vector3 proxyPos = _lastPeerPos - Vector3.up * CoopAvatar.GroundOffset();
                        _clientProxy.position = Vector3.Lerp(_clientProxy.position, proxyPos, Time.deltaTime * NetConfig.InterpSpeed);
                        if (!_clientProxy.gameObject.activeSelf) _clientProxy.gameObject.SetActive(true);
                    }
                    break;

                case "enemy":
                    if (IsClient && m.i >= 0 && m.i < _enemies.Count)
                    {
                        var e = _enemies[m.i];
                        if (e != null)
                        {
                            e.transform.position = Vector3.Lerp(e.transform.position, new Vector3(m.ex, m.ey, m.ez), Time.deltaTime * NetConfig.InterpSpeed);
                            e.transform.rotation = Quaternion.Slerp(e.transform.rotation, Quaternion.Euler(0f, m.ery, 0f), Time.deltaTime * NetConfig.InterpSpeed);
                            if (e.animator != null)
                            {
                                e.animator.SetFloat("MoveSpeed", m.espd, 0.2f, Time.deltaTime);
                                e.animator.SetInteger("ActionId", m.eact);
                                e.animator.SetBool("KillPlayer", m.ekill != 0);
                            }
                        }
                    }
                    break;

                case "obj":
                    if (IsClient && _gc != null)
                    {
                        _gc.m_currenPicturestCount = m.pics;
                        if (_gc.m_currentPapersCountText != null)
                            _gc.m_currentPapersCountText.text = m.pics + "/" + _gc.m_needPicturesCount;
                        _gc.m_eyesCount = m.pills;
                    }
                    break;

                case "ev":
                    HandleEvent(m);
                    break;
            }
        }

        void SendHello()
        {
            int avatar = (Role == CoopRole.Host) ? 0 : 1;
            if (_ws != null) _ws.Send(JsonUtility.ToJson(new NetMsg { t = "hello", pid = GameBackend.PlayerId, name = GameBackend.PlayerName, avatar = avatar }));
        }

        void ResendStartIfInGame()
        {
            string sc = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(sc)) return;
            string low = sc.ToLower();
            if (low.Contains("menu") || low.Contains("load") || low.Contains("start")) return;
            BroadcastStart(sc, PlayerPrefs.GetInt("EnemyMode", 0), PlayerPrefs.GetInt("GameDifficulty", 0));
        }

        /// <summary>
        /// The host pressed 开始游戏: mirror exactly what MainMenu.StartGame()
        /// does locally (same PlayerPrefs keys, same loading scene) so both
        /// peers end up in the same level.
        /// </summary>
        // the MainMenu scene ships m_loadingSceneName = "LoadScene"; used only as a
        // fallback when the MainMenu object cannot be found at that moment.
        const string FallbackLoadingScene = "LoadScene";

        void ApplyRemoteStart(string sceneName, int enemyMode, int difficulty)
        {
            if (!IsClient || string.IsNullOrEmpty(sceneName)) return;

            // the host retries until we report loaded, so ignore the repeats
            if (sceneName == _lastStartScene && Time.unscaledTime - _lastStartAt < 30f)
                return;
            _lastStartScene = sceneName;
            _lastStartAt = Time.unscaledTime;

            NetConfig.Log("remote start -> " + sceneName + " enemy=" + enemyMode + " diff=" + difficulty);

            PlayerPrefs.SetInt("EnemyMode", enemyMode);
            PlayerPrefs.SetString("GameLevel", sceneName);
            PlayerPrefs.SetInt("GameDifficulty", difficulty);
            PlayerPrefs.Save();

            MainMenu mm = FindObjectOfType<MainMenu>();
            string loading = (mm != null) ? mm.m_loadingSceneName : null;
            if (string.IsNullOrEmpty(loading)) loading = FallbackLoadingScene;

            if (!Application.CanStreamedLevelBeLoaded(loading))
            {
                NetConfig.LogError("remote start: '" + loading + "' not in build settings, loading level directly");
                Status = "房主已开始，载入中…";
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return;
            }

            Status = "房主已开始，载入中…";
            SceneManager.LoadScene(loading, LoadSceneMode.Single);
        }

        void HandleEvent(NetMsg m)
        {
            switch (m.k)
            {
                case "pickup": RemoveItemNear(m.px, m.py, m.pz, m.s); break;
                case "door": ApplyDoor(m.px, m.py, m.pz, m.i, m.s); break;
                case "adoor": ApplyAutoDoor(m.px, m.py, m.pz, m.i, m.s); break;
                case "anim": if (_avatar != null) _avatar.PlayAction(m.s); break;
                case "pic": if (IsHost && _gc != null) _gc.AddPaperPicture(); break;
                case "pill": if (IsHost && _gc != null) _gc.AddEyePills(1); break;
                case "noise": if (IsHost) ApplyNoise(m.px, m.py, m.pz); break;
                case "caught": if (IsClient && !_localDead) TriggerLocalCaught(); break;
                case "start": ApplyRemoteStart(m.s, m.enemyMode, m.difficulty); break;
                case "loaded":
                    if (IsHost && _awaitingPeerLoaded)
                    {
                        _awaitingPeerLoaded = false;
                        NetConfig.Log("peer reported loaded, start handshake complete");
                    }
                    break;
                case "dead":
                    _peerDead = true;
                    if (_avatar != null) _avatar.PlayDeath();
                    if (_localDead) EndGame();
                    break;
                case "win":
                    if (_gc != null) { IsApplyingRemote = true; _gc.GameWin(); IsApplyingRemote = false; }
                    break;
                case "lose":
                    if (_gc != null) { IsApplyingRemote = true; ForceLocalGameOver = true; _gc.GameOver(); ForceLocalGameOver = false; IsApplyingRemote = false; }
                    break;
            }
        }

        // ------------------------------------------------------------ death / spectate
        public void OnLocalDeath()
        {
            if (_localDead) return;
            _localDead = true;
            SendEv("dead");
            if (!_peerDead) EnterSpectate();
            else EndGame();
        }

        void EnterSpectate()
        {
            if (_player != null)
            {
                _player.locked = true;
                _player.lockedByDying = true;
                var cc = _player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _player.enabled = false;
            }
            Status = "你已阵亡 · 观战中";
        }

        void EndGame()
        {
            ForceLocalGameOver = true;
            if (_gc != null) _gc.GameOver();
            ForceLocalGameOver = false;
        }

        void TriggerLocalCaught()
        {
            if (_player != null)
            {
                _player.locked = true;
                _player.CatchPlayer(1);
                _player.CatchPlayer(2);
            }
            else OnLocalDeath();
        }

        void SpectateUpdate()
        {
            if (_player == null || _avatar == null) return;
            Vector3 target = _avatar.transform.position;
            Vector3 pos = target - _avatar.transform.forward * 3.5f + Vector3.up * 2.2f;
            _player.transform.position = Vector3.Lerp(_player.transform.position, pos, Time.deltaTime * 4f);
            Vector3 look = target + Vector3.up * 1.0f;
            Vector3 dir = look - _player.transform.position;
            if (dir.sqrMagnitude > 0.001f)
                _player.transform.rotation = Quaternion.Slerp(_player.transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 6f);
            if (_player.cameraTransform != null) _player.cameraTransform.localRotation = Quaternion.identity;
        }

        public void NotifyClientCaught()
        {
            if (!IsHost || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "caught" }));
        }

        // ------------------------------------------------------------ reports from the client
        public void ReportPicture() { SendEv("pic"); }
        public void ReportPill() { SendEv("pill"); }
        public void ReportNoise(Vector3 p)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "noise", px = p.x, py = p.y, pz = p.z }));
        }

        void ApplyNoise(float x, float y, float z)
        {
            var pos = new Vector3(x, y, z);
            foreach (var e in _enemies) if (e != null) e.CallEnemy(pos);
        }

        // ------------------------------------------------------------ broadcasts
        public void BroadcastPickup(Vector3 pos, string id)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "pickup", px = pos.x, py = pos.y, pz = pos.z, s = id }));
        }
        public void BroadcastPickupAnim(Vector3 pos)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "anim", s = "pickup", px = pos.x, py = pos.y, pz = pos.z }));
        }
        public void BroadcastDoor(Vector3 pos, int state, string id)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "door", px = pos.x, py = pos.y, pz = pos.z, i = state, s = id }));
        }
        public void BroadcastAutoDoor(Vector3 pos, int state, string id)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "adoor", px = pos.x, py = pos.y, pz = pos.z, i = state, s = id }));
        }
        public void BroadcastObjective(int pics, int pills)
        {
            if (!IsHost || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "obj", pics = pics, pills = pills }));
        }
        public void BroadcastStart(string sceneName, int enemyMode, int difficulty)
        {
            if (!IsHost || _ws == null) return;
            SendStart(sceneName, enemyMode, difficulty);

            _startScene = sceneName;
            _startEnemy = enemyMode;
            _startDiff = difficulty;
            _awaitingPeerLoaded = true;
            _startResendCount = 0;
            _startResendAt = Time.unscaledTime + 2f;
            NetConfig.Log("start sent -> " + sceneName + " (retrying until peer reports loaded)");
        }

        void SendStart(string sceneName, int enemyMode, int difficulty)
        {
            if (_ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = "start", s = sceneName,
                                                     enemyMode = enemyMode, difficulty = difficulty }));
        }

        /// <summary>Re-send "start" every 2 s until the peer confirms it loaded.</summary>
        void TickStartRetry()
        {
            if (!_awaitingPeerLoaded) return;
            if (_ws == null || !_ws.Connected) return;
            if (Time.unscaledTime < _startResendAt) return;

            _startResendAt = Time.unscaledTime + 2f;
            if (_startResendCount >= 10)
            {
                _awaitingPeerLoaded = false;
                NetConfig.LogError("peer never reported loaded, giving up on start retries");
                return;
            }
            _startResendCount++;
            NetConfig.Log("start re-sent #" + _startResendCount);
            SendStart(_startScene, _startEnemy, _startDiff);
        }
        public void BroadcastWin() { SendEv("win"); }
        public void BroadcastLose() { SendEv("lose"); }
        void SendEv(string k)
        {
            if (!IsCoop || _ws == null) return;
            _ws.Send(JsonUtility.ToJson(new NetMsg { t = "ev", k = k }));
        }

        // ------------------------------------------------------------ lookups
        void RemoveItemNear(float x, float y, float z, string id)
        {
            var target = new Vector3(x, y, z);
            GameObject best = null;
            float bestD = 0.5f * 0.5f;

            foreach (var it in FindObjectsOfType<Item>())
            {
                if (!string.IsNullOrEmpty(id) && PathId(it.transform) == id) { best = it.gameObject; break; }
                float d = (it.transform.position - target).sqrMagnitude;
                if (d < bestD) { bestD = d; best = it.gameObject; }
            }
            if (best == null)
            {
                foreach (var pp in FindObjectsOfType<PicturePaper>())
                {
                    if (!string.IsNullOrEmpty(id) && PathId(pp.transform) == id) { best = pp.gameObject; break; }
                    float d = (pp.transform.position - target).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = pp.gameObject; }
                }
            }
            if (best != null) Destroy(best);
        }

        void ApplyDoor(float x, float y, float z, int state, string id)
        {
            var target = new Vector3(x, y, z);
            Door best = null;
            float bestD = 1.0f;
            foreach (var d in FindObjectsOfType<Door>())
            {
                if (!string.IsNullOrEmpty(id) && PathId(d.transform) == id) { best = d; break; }
                float dd = (d.transform.position - target).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = d; }
            }
            if (best != null) best.NetApply(state == 1);
        }

        void ApplyAutoDoor(float x, float y, float z, int state, string id)
        {
            var target = new Vector3(x, y, z);
            AutomaticDoor best = null;
            float bestD = 2.0f;
            foreach (var d in FindObjectsOfType<AutomaticDoor>())
            {
                if (!string.IsNullOrEmpty(id) && PathId(d.transform) == id) { best = d; break; }
                float dd = (d.transform.position - target).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = d; }
            }
            if (best == null) return;
            if (state == 9) best.NetApplyUnlock();
            else best.NetApplyState(state);
        }
    }
}
