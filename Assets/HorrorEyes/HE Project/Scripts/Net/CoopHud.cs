using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GrannyCoop
{
    /// <summary>
    /// Runtime-built co-op UI. Nothing is added to the scenes by hand:
    ///  - in the MainMenu scene: a centred panel to host / join a room
    ///  - in the game scene: a one-line status bar (room code + connection)
    ///
    /// Everything is built defensively: the built-in font name changed in
    /// Unity 2022.2 (Arial.ttf -> LegacyRuntime.ttf), the EventSystem in the
    /// shipped scene may lack an input module, and a silent early-return in a
    /// click handler looks exactly like "the button does nothing" on device.
    /// </summary>
    public class CoopHud : MonoBehaviour
    {
        Canvas _canvas;
        GameObject _menuPanel;
        GameObject _gameBar;
        Text _statusText;
        Text _gameStatusText;
        InputField _codeInput;
        Font _font;
        bool _busy;
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
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
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
            const float W = 700f;
            _menuPanel = MakePanel(_canvas.transform, "CoopMenu", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(W, 640f), new Color(0.05f, 0.05f, 0.07f, 0.90f), false);
            // centred panel: its children anchor to its own top-centre
            var prt = _menuPanel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);

            MakeTextAt(_menuPanel.transform, "双人联机", 46, TextAnchor.UpperCenter, Color.white,
                new Vector2(0, -12), new Vector2(W - 20, 60));

            MakeButton(_menuPanel.transform, "创建房间", new Vector2(-170, -90), new Vector2(300, 96), OnCreate);
            MakeButton(_menuPanel.transform, "加入房间", new Vector2(170, -90), new Vector2(300, 96), OnJoin);

            MakeTextAt(_menuPanel.transform, "房间码（4~6 位数字）", 28, TextAnchor.UpperCenter,
                new Color(0.75f, 0.75f, 0.8f), new Vector2(0, -200), new Vector2(W - 40, 40));

            _codeInput = MakeInput(_menuPanel.transform, new Vector2(0, -246), new Vector2(360, 92));
            _codeInput.text = "";

            MakeButton(_menuPanel.transform, "退出联机", new Vector2(0, -360), new Vector2(300, 88), OnLeave);

            _statusText = MakeTextAt(_menuPanel.transform, "", 28, TextAnchor.UpperCenter,
                new Color(0.7f, 1f, 0.7f), new Vector2(0, -470), new Vector2(W - 30, 150));
            UpdateStatus();
        }

        void BuildGameBar()
        {
            _gameBar = MakePanel(_canvas.transform, "CoopBar", new Vector2(0f, 1f),
                new Vector2(20, -20), new Vector2(620, 76), new Color(0.05f, 0.05f, 0.07f, 0.6f), false);
            _gameStatusText = MakeLabel(_gameBar.transform, "", 30, TextAnchor.MiddleLeft, Color.white);
            UpdateStatus();
        }

        void Update() { UpdateStatus(); }

        void UpdateStatus()
        {
            var s = CoopSession.Instance;
            string txt;
            if (s == null) txt = "联机模块未初始化";
            else if (s.Role == CoopRole.None) txt = "当前：单机模式";
            else
            {
                string role = s.Role == CoopRole.Host ? "房主" : "加入方";
                txt = "房间 " + s.RoomCode + " · " + role + " · " + (s.PeerReady ? "已连接" : s.Status);
            }
            if (!string.IsNullOrEmpty(_feedback)) txt += "\n" + _feedback;
            if (_statusText != null) _statusText.text = txt;
            if (_gameStatusText != null) _gameStatusText.text = txt;
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
            _busy = true;
            StartCoroutine(s.StartHost("Player", (ok, err) =>
            {
                _busy = false;
                Note(ok ? "创建房间成功" : "创建失败：" + err);
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
            _busy = true;
            StartCoroutine(s.StartClient(code, "Player", (ok, err) =>
            {
                _busy = false;
                Note(ok ? "加入房间成功" : "加入失败：" + err);
            }));
        }

        void OnLeave()
        {
            Note("已点击：退出联机");
            var s = CoopSession.Instance;
            if (s != null) s.StopCoop();
        }
    }
}
