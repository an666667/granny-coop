using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GrannyCoop
{
    /// <summary>
    /// Runtime-built co-op UI. Nothing is added to the scenes by hand:
    ///  - in the MainMenu scene: a small panel to host / join a room
    ///  - in the game scene: a one-line status bar (room code + connection)
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

        // ------------------------------------------------------------ build
        void BuildCanvas()
        {
            var go = new GameObject("CoopCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        GameObject MakePanel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        Text MakeText(Transform parent, string txt, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 4); rt.offsetMax = new Vector2(-8, -4);
            var t = go.AddComponent<Text>();
            t.font = _font; t.text = txt; t.fontSize = size; t.alignment = anchor; t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = MakePanel("Btn_" + label, anchorMin, anchorMax, pivot, pos, size, new Color(0.15f, 0.15f, 0.18f, 0.95f));
            go.transform.SetParent(parent, false);
            var btn = go.AddComponent<Button>();
            var colors = btn.colors; colors.normalColor = Color.white; btn.colors = colors;
            MakeText(go.transform, label, 34, TextAnchor.MiddleCenter, Color.white);
            btn.onClick.AddListener(onClick);
            return btn;
        }

        InputField MakeInput(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = MakePanel("CodeInput", anchorMin, anchorMax, pivot, pos, size, new Color(1, 1, 1, 0.95f));
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<InputField>();
            var txt = MakeText(go.transform, "", 34, TextAnchor.MiddleCenter, Color.black);
            field.textComponent = txt;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.characterLimit = 6;
            return field;
        }

        void Rebuild()
        {
            if (_canvas == null) return;
            if (_menuPanel != null) Destroy(_menuPanel);
            if (_gameBar != null) Destroy(_gameBar);

            string scene = SceneManager.GetActiveScene().name;
            bool inMenu = scene.ToLower().Contains("menu");
            bool inGame = !inMenu && !scene.ToLower().Contains("load") && !scene.ToLower().Contains("start");

            if (inMenu) BuildMenuPanel();
            else if (inGame) BuildGameBar();
        }

        void BuildMenuPanel()
        {
            _menuPanel = MakePanel("CoopMenu", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -20), new Vector2(520, 400), new Color(0.05f, 0.05f, 0.07f, 0.85f));

            MakeText(_menuPanel.transform, "双人联机", 42, TextAnchor.UpperCenter, Color.white)
                .GetComponent<RectTransform>().offsetMax = new Vector2(-8, -8);

            MakeButton(_menuPanel.transform, "创建房间", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -80), new Vector2(220, 80), OnCreate);
            MakeButton(_menuPanel.transform, "加入房间", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(260, -80), new Vector2(220, 80), OnJoin);

            _codeInput = MakeInput(_menuPanel.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -180), new Vector2(220, 80));
            _codeInput.text = "";
            MakeText(_menuPanel.transform, "输入房间码", 28, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f))
                .GetComponent<RectTransform>().offsetMin = new Vector2(260, 0);

            MakeButton(_menuPanel.transform, "退出联机", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -280), new Vector2(220, 80), OnLeave);

            _statusText = MakeText(_menuPanel.transform, "", 30, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.7f));
            _statusText.GetComponent<RectTransform>().offsetMin = new Vector2(16, 0);
            _statusText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 60);
            UpdateStatus();
        }

        void BuildGameBar()
        {
            _gameBar = MakePanel("CoopBar", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -20), new Vector2(560, 70), new Color(0.05f, 0.05f, 0.07f, 0.6f));
            _gameStatusText = MakeText(_gameBar.transform, "", 30, TextAnchor.MiddleLeft, Color.white);
            UpdateStatus();
        }

        void Update()
        {
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var s = CoopSession.Instance;
            string txt;
            if (s == null || s.Role == CoopRole.None) txt = "单机模式";
            else
            {
                string role = s.Role == CoopRole.Host ? "房主" : "加入方";
                txt = "房间 " + s.RoomCode + " · " + role + " · " + (s.PeerReady ? "已连接" : s.Status);
            }
            if (_statusText != null) _statusText.text = txt;
            if (_gameStatusText != null) _gameStatusText.text = txt;
        }

        // ------------------------------------------------------------ actions
        void OnCreate()
        {
            if (_busy) return;
            var s = CoopSession.Instance;
            if (s == null) return;
            _busy = true;
            StartCoroutine(s.StartHost("Player", (ok, err) => { _busy = false; if (!ok) NetConfig.LogError(err); }));
        }

        void OnJoin()
        {
            if (_busy) return;
            var s = CoopSession.Instance;
            if (s == null) return;
            string code = _codeInput != null ? _codeInput.text.Trim() : "";
            if (string.IsNullOrEmpty(code)) return;
            _busy = true;
            StartCoroutine(s.StartClient(code, "Player", (ok, err) => { _busy = false; if (!ok) NetConfig.LogError(err); }));
        }

        void OnLeave()
        {
            var s = CoopSession.Instance;
            if (s != null) s.StopCoop();
        }
    }
}
