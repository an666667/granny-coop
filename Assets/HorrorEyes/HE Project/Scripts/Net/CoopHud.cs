using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GrannyCoop
{
    /// <summary>
    /// Runtime-built co-op UI. Nothing is added to the scenes by hand.
    ///
    /// MainMenu scene has two views:
    ///   - lobby        : create / join a room
    ///   - waiting room : big room code + connection state + 开始游戏 (host)
    /// The game scene shows a one-line status bar.
    ///
    /// Built defensively: the built-in font name changed in Unity 2022.2
    /// (Arial.ttf -> LegacyRuntime.ttf), the EventSystem shipped in the scene
    /// may lack an input module, and a silent early-return in a click handler
    /// is indistinguishable from "the button is broken" on a device.
    /// </summary>
    public class CoopHud : MonoBehaviour
    {
        Canvas _canvas;
        GameObject _menuPanel;
        GameObject _gameBar;
        Text _statusText;
        Text _gameStatusText;
        Text _codeText;
        Button _startButton;
        InputField _codeInput;
        Font _font;
        bool _busy;
        float _busySince;
        bool _shownInRoom;
        string _feedback = "";

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnScene;
        }
        void OnDestroy() { SceneManager.sceneLoaded -= OnScene; }

        void Start()
        {
            BuildCanvas();
            Rebuild();
        }

        void OnScene(Scene s, LoadSceneMode m) { StartCoroutine(RebuildNextFrame()); }
        IEnumerator RebuildNextFrame() { yield return null; Rebuild(); }

        // ------------------------------------------------------------- font
        // Unity 2022.2+ renamed the built-in font. Asking for the old name
        // logs an error and returns null, which leaves every Text blank and
        // makes the whole panel look dead.
        static Font LoadFont()
        {
            string[] names = { "LegacyRuntime.ttf", "Arial.ttf" };
            foreach (string n in names)
            {
                try
                {
                    Font f = Resources.GetBuiltinResource<Font>(n);
                    if (f != null) return f;
                }
                catch { }
            }
            try
            {
                Font os = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Noto Sans CJK SC", "Droid Sans Fallback", "Roboto", "Arial", "sans-serif" }, 36);
                if (os != null) return os;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ build
        void BuildCanvas()
        {
            var go = new GameObject("CoopCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            _font = LoadFont();

            // Build stamp. With several APKs in circulation, "is the phone
            // actually running the new build?" has cost whole round trips, so
            // the version now lives permanently on screen.
            MakeLabel(_canvas.transform, "build " + Application.version, 22,
                TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.6f));
        }

        // The scene ships an EventSystem object, but its module may be missing
        // or disabled. FindObjectOfType only returns active components, so a
        // found EventSystem is not proof that UI input actually works.
        void EnsureEventSystem()
        {
            EventSystem es = null;
            try { es = FindObjectOfType<EventSystem>(); } catch { }
            if (es == null)
            {
                var go = new GameObject("CoopEventSystem");
                go.transform.SetParent(transform, false);
                es = go.AddComponent<EventSystem>();
            }
            if (es.GetComponent<BaseInputModule>() == null)
                es.gameObject.AddComponent<StandaloneInputModule>();
            if (!es.enabled) es.enabled = true;
        }

        // --------------------------------------------------------- primitives
        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        GameObject MakePanel(Transform parent, string name, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool raycast)
        {
            var go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            // The pivot doubles as the anchor point, otherwise every panel that
            // is not meant to be top-centre lands at the top of the screen.
            rt.anchorMin = rt.anchorMax = pivot;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return go;
        }

        Text MakeTextAt(Transform parent, string txt, int fontSize, TextAnchor align, Color color, Vector2 pos, Vector2 size)
        {
            var go = NewUI("Text", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = txt;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // Label stretched to fill its button, never intercepting the tap.
        Text MakeLabel(Transform parent, string txt, int fontSize, TextAnchor align, Color color)
        {
            var go = NewUI("Label", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 4);
            rt.offsetMax = new Vector2(-6, -4);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = txt;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = MakePanel(parent, "Btn_" + label, new Vector2(0.5f, 1f), pos, size,
                new Color(0.16f, 0.16f, 0.20f, 0.98f), true);
            var img = go.GetComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            MakeLabel(go.transform, label, 36, TextAnchor.MiddleCenter, Color.white);
            if (onClick != null) btn.onClick.AddListener(onClick);
            return btn;
        }

        InputField MakeInput(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = MakePanel(parent, "CodeInput", new Vector2(0.5f, 1f), pos, size,
                new Color(1f, 1f, 1f, 0.98f), true);
            var img = go.GetComponent<Image>();
            var field = go.AddComponent<InputField>();
            var txt = MakeLabel(go.transform, "", 36, TextAnchor.MiddleCenter, Color.black);
            txt.supportRichText = false;
            field.targetGraphic = img;
            field.textComponent = txt;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 6;
            return field;
        }

        // ------------------------------------------------------------ layout
        void Rebuild()
        {
            if (_canvas == null) return;
            if (_menuPanel != null) { Destroy(_menuPanel); _menuPanel = null; }
            if (_gameBar != null) { Destroy(_gameBar); _gameBar = null; }

            string scene = SceneManager.GetActiveScene().name;
            string low = scene.ToLowerInvariant();
            bool inMenu = low.Contains("menu");
            bool inGame = !inMenu && !low.Contains("load") && !low.Contains("start");

            if (inMenu) BuildMenuPanel();
            else if (inGame) BuildGameBar();
        }

        void BuildMenuPanel()
        {
            if (_menuPanel != null) { Destroy(_menuPanel); _menuPanel = null; }
            _codeText = null;
            _startButton = null;
            _codeInput = null;
            _statusText = null;

            var s = CoopSession.Instance;
            _shownInRoom = s != null && s.Role != CoopRole.None;
            if (_shownInRoom) BuildWaitingRoom(s);
            else BuildLobby();
        }

        const float W = 700f;

        GameObject NewCentredPanel(float h)
        {
            var panel = MakePanel(_canvas.transform, "CoopMenu", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(W, h), new Color(0.05f, 0.05f, 0.07f, 0.90f), false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            return panel;
        }

        void BuildLobby()
        {
            _menuPanel = NewCentredPanel(740f);

            MakeTextAt(_menuPanel.transform, "双人联机", 46, TextAnchor.UpperCenter, Color.white,
                new Vector2(0, -12), new Vector2(W - 20, 60));

            MakeButton(_menuPanel.transform, "创建房间", new Vector2(-170, -90), new Vector2(300, 96), OnCreate);
            MakeButton(_menuPanel.transform, "加入房间", new Vector2(170, -90), new Vector2(300, 96), OnJoin);

            MakeTextAt(_menuPanel.transform, "房间码（4~6 位数字）", 28, TextAnchor.UpperCenter,
                new Color(0.75f, 0.75f, 0.8f), new Vector2(0, -200), new Vector2(W - 40, 40));

            _codeInput = MakeInput(_menuPanel.transform, new Vector2(0, -246), new Vector2(360, 92));
            _codeInput.text = "";

            MakeButton(_menuPanel.transform, "退出联机", new Vector2(0, -360), new Vector2(300, 88), OnLeave);

            MakeButton(_menuPanel.transform, "测试服务器", new Vector2(0, -462), new Vector2(300, 84), OnPing);

            _statusText = MakeTextAt(_menuPanel.transform, "", 26, TextAnchor.UpperCenter,
                new Color(0.7f, 1f, 0.7f), new Vector2(0, -560), new Vector2(W - 30, 150));
            UpdateStatus();
        }

        void BuildWaitingRoom(CoopSession s)
        {
            bool isHost = s.Role == CoopRole.Host;
            _menuPanel = NewCentredPanel(720f);

            MakeTextAt(_menuPanel.transform, isHost ? "等待房间（房主）" : "等待房间（加入方）", 42,
                TextAnchor.UpperCenter, Color.white, new Vector2(0, -12), new Vector2(W - 20, 56));

            MakeTextAt(_menuPanel.transform, "把房间码报给队友", 28, TextAnchor.UpperCenter,
                new Color(0.75f, 0.75f, 0.8f), new Vector2(0, -84), new Vector2(W - 40, 36));

            // the code itself, big enough to read out loud
            _codeText = MakeTextAt(_menuPanel.transform, s.RoomCode, 132, TextAnchor.MiddleCenter,
                new Color(1f, 0.95f, 0.55f), new Vector2(0, -124), new Vector2(W - 40, 170));

            _statusText = MakeTextAt(_menuPanel.transform, "", 34, TextAnchor.UpperCenter,
                new Color(0.7f, 1f, 0.7f), new Vector2(0, -300), new Vector2(W - 30, 120));

            if (isHost)
            {
                _startButton = MakeButton(_menuPanel.transform, "开始游戏", new Vector2(0, -420),
                    new Vector2(380, 108), OnStartGame);
            }
            else
            {
                MakeTextAt(_menuPanel.transform, "等待房主点「开始游戏」…", 30, TextAnchor.UpperCenter,
                    new Color(0.85f, 0.85f, 0.9f), new Vector2(0, -430), new Vector2(W - 30, 60));
            }

            MakeButton(_menuPanel.transform, "退出房间", new Vector2(0, -570), new Vector2(300, 88), OnLeave);

            UpdateStatus();
        }

        // ------------------------------------------------------------- icons
        // No SVG rasteriser is available in the build environment, so the round
        // button icons are drawn into textures here. The matching .svg sources
        // live in Assets/HorrorEyes/HE Project/UI/Icons/ for editing.
        static Sprite MakeIconSprite(int kind, Color bg, Color fg, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float cx = size * 0.5f, cy = size * 0.5f;
            float r = size * 0.5f - 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);      // antialiased rim
                    Color c = bg;
                    if (a > 0.01f && InGlyph(kind, dx / r, dy / r)) c = fg;
                    c.a *= a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>u,v are -1..1 within the circle; y up.</summary>
        static bool InGlyph(int kind, float u, float v)
        {
            if (kind == 0)   // jump: up arrow
            {
                if (Mathf.Abs(u) < 0.15f && v > -0.42f && v < 0.30f) return true;
                if (v >= 0.20f && v <= 0.62f && Mathf.Abs(u) <= (0.62f - v) * 1.10f) return true;
                return false;
            }
            if (kind == 1)   // prone: body lying flat, head on the left
            {
                float hx = u + 0.42f, hy = v - 0.12f;
                if (hx * hx + hy * hy < 0.080f) return true;
                if (Mathf.Abs(v + 0.02f) < 0.15f && u > -0.22f && u < 0.58f) return true;
                if (Mathf.Abs(v - 0.20f) < 0.06f && u > -0.18f && u < 0.30f) return true; // arm
                return false;
            }
            return false;
        }

        Sprite _iconJump, _iconProne;

        GameObject MakeIconButton(string name, Sprite icon, Vector2 pos, float size,
                                  UnityEngine.Events.UnityAction onClick)
        {
            var go = MakePanel(_canvas.transform, name, new Vector2(1f, 0f), pos,
                new Vector2(size, size), new Color(1f, 1f, 1f, 0f), true);
            var img = go.GetComponent<Image>();
            img.sprite = icon;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            return go;
        }

        void BuildGameBar()
        {
            _gameBar = MakePanel(_canvas.transform, "CoopBar", new Vector2(0f, 1f),
                new Vector2(20, -20), new Vector2(1040, 210), new Color(0.05f, 0.05f, 0.07f, 0.55f), false);
            _gameStatusText = MakeLabel(_gameBar.transform, "", 26, TextAnchor.UpperLeft, Color.white);
            BuildLookToggle();
            BuildJumpButton();
            UpdateStatus();
        }

        /// <summary>
        /// Bottom-right action buttons: jump and prone. The game ships neither,
        /// and RunButton sits at 74% height, so the lower right is free.
        /// </summary>
        void BuildJumpButton()
        {
            if (_iconJump == null)
                _iconJump = MakeIconSprite(0, new Color(0.20f, 0.62f, 0.30f, 0.95f), Color.white);
            if (_iconProne == null)
                _iconProne = MakeIconSprite(1, new Color(0.24f, 0.42f, 0.78f, 0.95f), Color.white);

            MakeIconButton("Btn_Jump", _iconJump, new Vector2(-40f, 70f), 200f, OnJump);
            MakeIconButton("Btn_Prone", _iconProne, new Vector2(-260f, 70f), 200f, OnProne);
        }

        void OnJump()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            p.RequestJump();
        }

        void OnProne()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            p.RequestProne();
            Note(p.prone ? "趴下" : "起身");
        }

        Text _lookYawText, _lookPitchText;

        /// <summary>
        /// Swipe-look direction is a taste thing, and yaw/pitch are independent
        /// (one can be direct while the other is flipped), so both get their own
        /// on-device button. Choices are remembered across launches, which saves
        /// a 20 minute rebuild every time the feel needs adjusting.
        /// </summary>
        void BuildLookToggle()
        {
            _lookYawText = MakeLookButton("视角左右", 70f, true);
            _lookPitchText = MakeLookButton("视角上下", -70f, false);
            RefreshLookLabels();
        }

        Text MakeLookButton(string name, float y, bool yaw)
        {
            var go = MakePanel(_canvas.transform, "Btn_" + name, new Vector2(1f, 0.5f),
                new Vector2(-24f, y), new Vector2(300f, 84f), new Color(0.16f, 0.16f, 0.20f, 0.85f), true);
            var img = go.GetComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var txt = MakeLabel(go.transform, "", 26, TextAnchor.MiddleCenter, Color.white);
            if (yaw) btn.onClick.AddListener(OnToggleYaw);
            else btn.onClick.AddListener(OnTogglePitch);
            return txt;
        }

        void RefreshLookLabels()
        {
            if (_lookYawText != null)
                _lookYawText.text = "视角左右 " + (TouchLookInput.InvertYaw ? "反向" : "正向");
            if (_lookPitchText != null)
                _lookPitchText.text = "视角上下 " + (TouchLookInput.InvertPitch ? "反向" : "正向");
        }

        void OnToggleYaw()
        {
            TouchLookInput.InvertYaw = !TouchLookInput.InvertYaw;
            TouchLookInput.SavePrefs();
            RefreshLookLabels();
            Note("视角左右：" + (TouchLookInput.InvertYaw ? "反向" : "正向") + "（已记住）");
        }

        void OnTogglePitch()
        {
            TouchLookInput.InvertPitch = !TouchLookInput.InvertPitch;
            TouchLookInput.SavePrefs();
            RefreshLookLabels();
            Note("视角上下：" + (TouchLookInput.InvertPitch ? "反向" : "正向") + "（已记住）");
        }

        /// <summary>
        /// Extra on-screen numbers so avatar problems are visible on the device
        /// instead of needing logcat: does the peer avatar exist, is it active,
        /// where is it, how far is it, and what did the model fitting measure.
        /// </summary>
        string BuildDiagnostics()
        {
            var s = CoopSession.Instance;
            if (s == null) return "";
            var sb = new System.Text.StringBuilder();

            var av = s.PeerAvatarObject;
            if (av == null)
            {
                sb.Append("对方角色: 无");
            }
            else
            {
                sb.Append("对方角色: ").Append(av.gameObject.activeSelf ? "显示" : "隐藏");
                Vector3 p = av.transform.position;
                sb.Append(" 位置(").Append(p.x.ToString("F1")).Append(",").Append(p.y.ToString("F1"))
                  .Append(",").Append(p.z.ToString("F1")).Append(")");
                if (s.HasLocalPlayer)
                {
                    float d = Vector3.Distance(p, s.LocalPlayerPos);
                    sb.Append(" 距离").Append(d.ToString("F1"));
                }
                sb.Append("\n  模型 ").Append(av.ModelLoaded ? av.ModelName : "未加载")
                  .Append(" 高").Append(av.MeasuredHeight.ToString("F2"))
                  .Append(" 对齐").Append(av.AlignOffset.ToString("F2"));
            }

            sb.Append("\n").Append(s.EnemyDiag());
            sb.Append("\n我(").Append(s.RoleName).Append("): ");
            if (s.HasLocalPlayer)
            {
                Vector3 lp = s.LocalPlayerPos;
                sb.Append("(").Append(lp.x.ToString("F1")).Append(",").Append(lp.y.ToString("F1"))
                  .Append(",").Append(lp.z.ToString("F1")).Append(")");
            }
            else sb.Append("找不到本地玩家");
            return sb.ToString();
        }

        void Update()
        {
            var s = CoopSession.Instance;
            bool nowInRoom = s != null && s.Role != CoopRole.None;

            // lobby <-> waiting room when the session starts or ends
            if (_menuPanel != null && nowInRoom != _shownInRoom) { BuildMenuPanel(); return; }

            // A request that never calls back must not leave the UI stuck on
            // "正在处理中" forever - surface it instead.
            if (_busy && Time.unscaledTime - _busySince > 20f)
            {
                _busy = false;
                Note("请求超时（20 秒无响应）");
            }

            UpdateStatus();
            EnsureLookWheelHidden();
        }

        float _wheelCheckAt;

        /// <summary>
        /// The game ships a right-hand look joystick ("JoystickLook") wired to the
        /// very same "Mouse X"/"Mouse Y" axes the camera uses. Swipe-to-look
        /// replaces it, so hide it - otherwise the wheel and the swipe fight over
        /// the same rotation, which reads as a wrong direction.
        /// Re-checked twice a second because the GameManager prefab is
        /// re-instantiated on every scene load.
        /// </summary>
        void EnsureLookWheelHidden()
        {
            if (Time.unscaledTime < _wheelCheckAt) return;
            _wheelCheckAt = Time.unscaledTime + 0.5f;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                var t = FindDeep(root.transform, "JoystickLook");
                if (t == null || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                NetConfig.Log("hid the built-in look joystick (swipe look replaces it)");
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        void UpdateStatus()
        {
            var s = CoopSession.Instance;
            string txt;
            if (s == null) txt = "联机模块未初始化";
            else if (s.Role == CoopRole.None) txt = "当前：单机模式";
            else if (s.PeerReady) txt = "已连接 2/2 · 可以开始";
            else txt = s.Status;

            if (!string.IsNullOrEmpty(_feedback)) txt += "\n" + _feedback;

            if (_statusText != null) _statusText.text = txt;
            if (_gameStatusText != null)
            {
                string d = BuildDiagnostics();
                _gameStatusText.text = string.IsNullOrEmpty(d) ? txt : (txt + "\n" + d);
            }
            if (_codeText != null && s != null) _codeText.text = s.RoomCode;
        }

        // Visible proof that a tap actually reached the UI layer.
        void Note(string msg)
        {
            _feedback = msg;
            NetConfig.Log("[CoopHud] " + msg);
            UpdateStatus();
        }

        // ----------------------------------------------------------- actions
        void OnCreate()
        {
            Note("已点击：创建房间");
            if (_busy) { Note("正在处理中，请稍候…"); return; }
            var s = CoopSession.Instance;
            if (s == null) { Note("联机模块未初始化（CoopSession 为空）"); return; }
            _busy = true; _busySince = Time.unscaledTime;
            StartCoroutine(s.StartHost("Player", (ok, err) =>
            {
                _busy = false;
                Note(ok ? "创建成功，进入等待房间" : "创建失败：" + err);
                BuildMenuPanel();
            }));
        }

        void OnJoin()
        {
            string code = _codeInput != null ? _codeInput.text.Trim() : "";
            Note("已点击：加入房间（输入=" + (string.IsNullOrEmpty(code) ? "空" : code) + "）");
            if (_busy) { Note("正在处理中，请稍候…"); return; }
            var s = CoopSession.Instance;
            if (s == null) { Note("联机模块未初始化（CoopSession 为空）"); return; }
            if (string.IsNullOrEmpty(code)) { Note("请先在上面输入房间码"); return; }
            _busy = true; _busySince = Time.unscaledTime;
            StartCoroutine(s.StartClient(code, "Player", (ok, err) =>
            {
                _busy = false;
                Note(ok ? "加入成功，进入等待房间" : "加入失败：" + err);
                BuildMenuPanel();
            }));
        }

        // Reuses the game's own Play button so the selected level / enemy /
        // difficulty are exactly the ones the host picked.
        void OnStartGame()
        {
            Note("已点击：开始游戏");
            var s = CoopSession.Instance;
            if (s == null) { Note("联机模块未初始化"); return; }
            var mm = FindObjectOfType<MainMenu>();
            if (mm == null) { Note("找不到主菜单脚本，请用原来的「开始游戏」按钮"); return; }
            mm.StartGame();
        }

        void OnPing()
        {
            Note("已点击：测试服务器");
            if (_busy) { Note("正在处理中，请稍候…"); return; }
            _busy = true; _busySince = Time.unscaledTime;
            StartCoroutine(GameBackend.Ping((ok, info) =>
            {
                _busy = false;
                Note(ok ? "服务器正常：" + info : "服务器异常：" + info);
            }));
        }

        void OnLeave()
        {
            Note("已点击：退出房间");
            var s = CoopSession.Instance;
            if (s != null) s.StopCoop();
            BuildMenuPanel();
        }
    }
}
