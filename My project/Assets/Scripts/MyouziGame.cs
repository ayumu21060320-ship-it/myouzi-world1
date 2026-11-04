using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 名字ワールドの画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）。
// 名字を入力する画面 → 世界地図の画面（地図のデータと絵は WorldMap.cs）。
public class MyouziGame : MonoBehaviour
{
    // プレイヤーが決めた名字（ほかの画面からも読めるようにしておく）
    public static string Surname { get; private set; } = "";

    const int MaxLength = 8;                 // 名字は 8 文字まで（仮）
    const string SaveKey = "Myouzi.Surname"; // 前回入れた名字を覚えておく場所

    static readonly Color Background = new Color(0.08f, 0.12f, 0.22f);
    static readonly Color Accent = new Color(0.95f, 0.75f, 0.25f);
    static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f);

    Font font;
    InputField nameInput;
    Text messageText;
    GameObject entryPanel;
    GameObject mapPanel;
    RectTransform mapRect;   // 地図の絵（拡大・移動はこれの大きさと位置を変える）
    RectTransform fitRect;   // 地図がちょうど収まる枠（拡大していないときの地図の大きさ）
    RectTransform areaRect;  // 地図を見せる窓（上下の帯の間）
    Canvas canvas;
    float zoom = 1f;
    bool dragging;           // 地図の上で右ボタンを押して、ドラッグしている途中
    const float MaxZoom = 8f;
    Text titleText, worldText, infoText;
    WorldMap map;
    int hoverId, selectId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<MyouziGame>() == null) new GameObject("MyouziGame").AddComponent<MyouziGame>();
    }

    void Start()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
        }

        // 日本語が出せるフォント。Assets/Resources/UIFont/ にフォントを置けばそれを使う（ブラウザ版ではこれが必要）。
        // 無ければ Windows に入っている日本語フォントを使う
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0]
             : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Hiragino Sans" }, 64);

        BuildUI();

        nameInput.text = PlayerPrefs.GetString(SaveKey, "");
        nameInput.ActivateInputField(); // すぐ文字を打てるようにする
    }

    // ================= 名字を決める =================

    void Confirm()
    {
        string name = (nameInput.text ?? "").Trim();
        if (name.Length == 0)
        {
            ShowMessage("名字を入力してください");
            nameInput.ActivateInputField();
            return;
        }
        if (name.Length > MaxLength)
        {
            ShowMessage(MaxLength + " 文字までにしてください");
            nameInput.ActivateInputField();
            return;
        }

        Surname = name;
        PlayerPrefs.SetString(SaveKey, name);
        PlayerPrefs.Save();

        OpenMap();
    }

    void BackToEntry()
    {
        mapPanel.SetActive(false);
        entryPanel.SetActive(true);
        nameInput.ActivateInputField();
    }

    void ShowMessage(string msg)
    {
        messageText.text = msg;
    }

    // ================= 世界地図 =================

    void OpenMap()
    {
        entryPanel.SetActive(false);
        mapPanel.SetActive(true);
        titleText.text = "「" + Surname + "」家の世界進出";
        if (map != null)
        {
            worldText.text = "世界の人口 " + WorldMap.FormatPeople(map.WorldPopulation)
                           + "\n「" + Surname + "」さん 0人";
        }
        ResetView();
        UpdateInfo();
    }

    void ResetView()
    {
        zoom = 1f;
        mapRect.localScale = Vector3.one;
        mapRect.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (map == null || mapPanel == null || !mapPanel.activeSelf) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        // マウスの下にある国を調べる
        WorldMap.Country c = null;
        bool onMap = false;
        bool inWindow = RectTransformUtility.RectangleContainsScreenPoint(areaRect, mouse.position.ReadValue(), null);
        if (inWindow && RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, mouse.position.ReadValue(), null, out var local))
        {
            var r = mapRect.rect;
            var uv = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height);
            onMap = uv.x >= 0 && uv.x < 1 && uv.y >= 0 && uv.y < 1;
            c = map.CountryAt(uv);
        }
        int hover = c != null ? c.id : 0;

        // ホイールで拡大・縮小（マウスの下の場所を動かさずに）。右ボタンを押しながらドラッグで移動
        float wheel = mouse.scroll.ReadValue().y;
        if (wheel != 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(fitRect, mouse.position.ReadValue(), null, out var cursor)
            && fitRect.rect.Contains(cursor))
        {
            float newZoom = Mathf.Clamp(zoom * (wheel > 0 ? 1.25f : 0.8f), 1f, MaxZoom);
            var pos = mapRect.anchoredPosition;
            var onMapPoint = (cursor - pos) / zoom;
            zoom = newZoom;
            mapRect.localScale = new Vector3(zoom, zoom, 1f);
            mapRect.anchoredPosition = ClampPan(cursor - onMapPoint * zoom);
        }
        if (mouse.rightButton.wasPressedThisFrame) dragging = inWindow;
        if (!mouse.rightButton.isPressed) dragging = false;
        if (dragging)
        {
            var d = mouse.delta.ReadValue() / canvas.scaleFactor;
            if (d != Vector2.zero) mapRect.anchoredPosition = ClampPan(mapRect.anchoredPosition + d);
        }

        // 地図の上のクリックでその国を選ぶ（海をクリックすると選ぶのをやめる）
        int select = selectId;
        if (mouse.leftButton.wasPressedThisFrame && onMap) select = hover;

        if (hover != hoverId || select != selectId)
        {
            hoverId = hover;
            selectId = select;
            map.SetHighlight(hoverId, selectId);
            UpdateInfo();
        }
    }

    // 地図の外の白い部分が見えないように、動かせる範囲を決める
    Vector2 ClampPan(Vector2 p)
    {
        var size = fitRect.rect.size;
        float mx = size.x * (zoom - 1f) / 2f;
        float my = size.y * (zoom - 1f) / 2f;
        return new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
    }

    // 左下の欄に、選んだ国（無ければマウスの下の国）の情報を出す
    void UpdateInfo()
    {
        if (map == null)
        {
            infoText.text = "地図のデータが見つかりません（Assets/Resources/Map/）";
            return;
        }
        var c = map.Get(selectId) ?? map.Get(hoverId);
        if (c == null)
        {
            infoText.text = "国にマウスを乗せると名前と人口。クリックで選ぶ\nホイールで拡大・縮小、右ドラッグで移動";
            return;
        }
        infoText.text = c.name + "（" + c.continent + "）\n人口 " + WorldMap.FormatPeople(c.population)
                      + "\n「" + Surname + "」さん 0人";
    }

    // 入力欄で Enter を押したときも決定する。
    // 日本語の変換を確定する Enter では入力が終わらないので、ここには来ない
    void OnEndEdit(string _)
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Confirm();
    }

    // ================= UI の組み立て =================

    void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("UI");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ---- 名字を入力する画面 ----
        entryPanel = MakePanel(canvasGo.transform, "EntryPanel");
        MakeText(entryPanel.transform, "名字ワールド", 72, Accent, new Vector2(0, 190), new Vector2(900, 100));
        MakeText(entryPanel.transform, "あなたの名字で、世界を染めよう。", 28, TextColor, new Vector2(0, 110), new Vector2(900, 50));
        MakeText(entryPanel.transform, "広めたい名字を入力してください（" + MaxLength + " 文字まで）", 24, TextColor, new Vector2(0, 30), new Vector2(900, 40));
        nameInput = MakeInput(entryPanel.transform, "例：山田", new Vector2(0, -40), new Vector2(420, 70));
        messageText = MakeText(entryPanel.transform, "", 22, new Color(1f, 0.5f, 0.45f), new Vector2(0, -105), new Vector2(900, 36));
        MakeButton(entryPanel.transform, "この名字で始める", new Vector2(0, -175), new Vector2(320, 70), Confirm);

        // ---- 世界地図の画面 ----
        map = WorldMap.Load();
        mapPanel = MakePanel(canvasGo.transform, "MapPanel");
        mapPanel.AddComponent<Image>().color = WorldMap.SeaColor; // 海と同じ白で画面全体を塗る

        // 地図は上の帯（70）と下の帯（110）の間に、縦横の比を保って最大の大きさで置く
        var area = MakePanel(mapPanel.transform, "MapArea");
        var areaRt = area.GetComponent<RectTransform>();
        areaRect = areaRt;
        areaRt.offsetMin = new Vector2(10, 110);
        areaRt.offsetMax = new Vector2(-10, -70);
        area.AddComponent<RectMask2D>(); // 拡大したとき、はみ出た分を隠す

        var fitGo = new GameObject("MapFit");
        fitGo.transform.SetParent(area.transform, false);
        fitRect = fitGo.AddComponent<RectTransform>();
        if (map != null)
        {
            var fitter = fitGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)map.Width / map.Height;
        }

        var mapGo = MakePanel(fitGo.transform, "Map");
        mapRect = mapGo.GetComponent<RectTransform>();
        var raw = mapGo.AddComponent<RawImage>();
        raw.raycastTarget = false;
        if (map != null) raw.texture = map.Texture;

        var header = MakeBar(mapPanel.transform, "Header", true, 70, Background);
        titleText = MakeText(header, "", 34, Accent, new Vector2(30, 0), new Vector2(700, 60), new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        MakeButton(header, "名字を変える", new Vector2(-20, 0), new Vector2(200, 50), BackToEntry, new Vector2(1, 0.5f));

        var footer = MakeBar(mapPanel.transform, "Footer", false, 110, Background);
        infoText = MakeText(footer, "", 26, TextColor, new Vector2(30, 0), new Vector2(760, 100), new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        worldText = MakeText(footer, "", 26, TextColor, new Vector2(-30, 0), new Vector2(440, 100), new Vector2(1, 0.5f), TextAnchor.MiddleRight);
        mapPanel.SetActive(false);
    }

    // 画面の上（top = true）か下に、高さ height の帯を置く
    Transform MakeBar(Transform parent, string name, bool top, float height, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, top ? 1 : 0);
        rt.anchorMax = new Vector2(1, top ? 1 : 0);
        rt.pivot = new Vector2(0.5f, top ? 1 : 0);
        rt.offsetMin = new Vector2(0, top ? -height : 0);
        rt.offsetMax = new Vector2(0, top ? 0 : height);
        go.AddComponent<Image>().color = color;
        return go.transform;
    }

    GameObject MakePanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go;
    }

    // 画面のまん中を (0, 0) として、pos の位置に size の大きさで置く
    // anchor を指定すると、画面のその場所（0,0 = 左下、1,1 = 右上）を基準にする。左端・右端に置くときは
    // pivot も同じにするので、pos はそこからのずれになる
    RectTransform Place(GameObject go, Transform parent, Vector2 pos, Vector2 size, Vector2? anchor = null)
    {
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    Text MakeText(Transform parent, string s, int size, Color color, Vector2 pos, Vector2 box,
                  Vector2? anchor = null, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Text");
        Place(go, parent, pos, box, anchor);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        t.text = s;
        return t;
    }

    InputField MakeInput(Transform parent, string placeholder, Vector2 pos, Vector2 box)
    {
        var go = new GameObject("NameInput");
        Place(go, parent, pos, box);
        var img = go.AddComponent<Image>();
        img.color = Color.white;
        var input = go.AddComponent<InputField>();

        Text Child(string name, Color c)
        {
            var cg = new GameObject(name);
            cg.transform.SetParent(go.transform, false);
            var rt = cg.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 4);
            rt.offsetMax = new Vector2(-12, -4);
            var t = cg.AddComponent<Text>();
            t.font = font;
            t.fontSize = 36;
            t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.supportRichText = false;
            return t;
        }
        var text = Child("Text", new Color(0.1f, 0.1f, 0.15f));
        var ph = Child("Placeholder", new Color(0.6f, 0.6f, 0.65f));
        ph.text = placeholder;
        input.textComponent = text;
        input.placeholder = ph;
        input.targetGraphic = img;
        input.lineType = InputField.LineType.SingleLine;
        input.onValueChanged.AddListener(_ => ShowMessage(""));
        input.onEndEdit.AddListener(OnEndEdit);
        return input;
    }

    void MakeButton(Transform parent, string label, Vector2 pos, Vector2 box, UnityEngine.Events.UnityAction onClick,
                    Vector2? anchor = null)
    {
        var go = new GameObject("Button");
        Place(go, parent, pos, box, anchor);
        var img = go.AddComponent<Image>();
        img.color = Accent;
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);

        var tg = new GameObject("Label");
        tg.transform.SetParent(go.transform, false);
        var rt = tg.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var t = tg.AddComponent<Text>();
        t.font = font;
        t.fontSize = 30;
        t.color = new Color(0.12f, 0.1f, 0.05f);
        t.alignment = TextAnchor.MiddleCenter;
        t.text = label;
    }
}
