using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 名字ワールドの画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）。
// 名字を入力する画面 → 世界地図の画面（地図のデータと絵は WorldMap.cs、広がり方のルールは NameSpread.cs）。
// 地図の画面は「最初の国を選ぶ」→「時間が進んで名字が広がる」の順に進む。
// 遊んでいる間の画面（ポイントのマーク・線・ニュース・強化パネル）は MyouziGamePlay.cs。
public partial class MyouziGame : MonoBehaviour
{
    // プレイヤーが決めた名字（ほかの画面からも読めるようにしておく）
    public static string Surname { get; private set; } = "";

    const int MaxLength = 12;                // 名字は 12 文字まで（ローマ字の名字も入るように）
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
    Text titleText, worldText, infoText, dateText;
    WorldMap map;
    int hoverId, selectId;

    // ---- ゲームの進み ----
    NameSpread spread;
    bool playing;            // false = 最初の国を選んでいるところ
    GameObject startButton;  // 「この国から始める」
    Text startLabel;
    Dictionary<int, double> knownStart = new Dictionary<int, double>(); // よくある名字なら、国ごとの最初の人数（珍しい名字なら空）
    GameObject speedButtons; // 停止・普通・速い
    Image[] speedImages;
    static readonly float[] Speeds = { 0f, 2f, 10f };          // 1 秒に進む日数
    static readonly string[] SpeedNames = { "停止", "普通", "速い" };
    int speedIndex = 1;
    float dayTimer;
    static readonly Color SpeedOff = new Color(0.45f, 0.48f, 0.55f);

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
        ResetView();

        // 最初の国を選ぶところから
        playing = false;
        selectId = 0;
        ClearEffects();
        SetPlayingLayout(false);
        if (map != null)
        {
            spread.Reset();
            map.ResetColors();
            map.SetHighlight(hoverId, 0);
            map.Flush();
        }
        // よくある名字なら、表にある国に最初から大勢いる。珍しい名字なら、最初の国を自分で選ぶ
        knownStart.Clear();
        if (map != null)
        {
            foreach (var kv in Surnames.Find(Surname))
            {
                var c = map.FindByCode(kv.Key);
                if (c != null) knownStart[c.id] = kv.Value;
            }
        }
        double startTotal = 0;
        foreach (var n in knownStart.Values) startTotal += n;
        if (knownStart.Count > 0)
        {
            titleText.text = "「" + Surname + "」さんは世界に約" + WorldMap.FormatPeople((long)startTotal);
            startLabel.text = "この人数で始める";
            foreach (var kv in knownStart)
                map.SetCountryColor(kv.Key, CountryColor(kv.Value / map.Get(kv.Key).population));
            map.Flush();
        }
        else
        {
            titleText.text = "珍しい名字！最初の" + NameSpread.RareStartPeople + "人の国を選ぼう";
            startLabel.text = "この国から始める";
        }
        worldText.text = (knownStart.Count > 0 ? "よくある名字: 最初から大勢いる\n" : "珍しい名字: 最初は少ない\n")
                       + "話題性 ×" + NameSpread.BuzzFor(knownStart.Count > 0 ? startTotal : NameSpread.RareStartPeople).ToString("F2")
                       + "（増える速さ）";
        dateText.text = "";
        startButton.SetActive(true);
        speedButtons.SetActive(false);
        UpdateInfo();
    }

    // 選んだ国で最初の 1 人が生まれ、時間が進み始める
    void StartGame()
    {
        if (map == null) return;
        if (knownStart.Count > 0)
        {
            spread.Start(knownStart);
        }
        else
        {
            if (map.Get(selectId) == null)
            {
                infoText.text = "先に地図で国をクリックして選んでください";
                return;
            }
            spread.Start(new Dictionary<int, double> { { selectId, NameSpread.RareStartPeople } });
        }
        playing = true;
        dayTimer = 0;
        SetSpeed(1);
        titleText.text = "「" + Surname + "」家の世界進出";
        startButton.SetActive(false);
        speedButtons.SetActive(true);
        SetPlayingLayout(true);
        AddNews("「" + Surname + "」家の世界進出が始まった！ 紫の「バズ」をクリックしてポイントを集めよう");
        RefreshColors();
        RefreshTexts();
        RefreshPanel();
    }

    void SetSpeed(int index)
    {
        speedIndex = index;
        for (int k = 0; k < speedImages.Length; k++) speedImages[k].color = k == index ? Accent : SpeedOff;
    }

    // 1 日ずつ進める
    void Tick()
    {
        dayTimer += Time.deltaTime * Speeds[speedIndex];
        int steps = 0;
        while (dayTimer >= 1f && steps < 30 && !spread.Over)
        {
            spread.Step();
            HandleDayEvents();
            dayTimer -= 1f;
            steps++;
        }
        if (dayTimer >= 1f) dayTimer = 0f; // 追いつけないほど遅れたら、残りは捨てる
        if (steps > 0)
        {
            RefreshColors();
            RefreshTexts();
            RefreshPanel();
        }
    }

    // 国の色: 名字の人の割合が増えるほど黄緑から紫に近づく。1 人でもいれば少し色が付く
    void RefreshColors()
    {
        foreach (var c in map.Countries)
        {
            double share = spread.Share(c);
            if (spread.People(c.id) < 1) continue;
            var color = CountryColor(share);
            if (spread.HasLaw(c.id)) color = Color32.Lerp(color, new Color32(120, 120, 120, 255), 0.5f); // 禁止法の国は灰色がかる
            map.SetCountryColor(c.id, color);
        }
    }

    static Color32 CountryColor(double share)
    {
        float t = Mathf.Lerp(0.2f, 1f, Mathf.Sqrt((float)share));
        t = Mathf.Round(t * 32f) / 32f; // 少しずつ変わるたびに描き直さないよう、32 段階にする
        return Color32.Lerp(WorldMap.LandColor, WorldMap.PlayerColor, t);
    }

    void RefreshTexts()
    {
        dateText.text = DateLabel();
        worldText.text = "「" + Surname + "」さん " + WorldMap.FormatPeople((long)spread.Total)
                       + "（世界の " + FormatShare(spread.WorldShare) + "）"
                       + "\n世界の人口 " + WorldMap.FormatPeople(spread.WorldPopulation)
                       + "\n" + spread.CountriesReached + "か国・話題性 ×" + spread.Buzz.ToString("F2");
        UpdateInfo();
    }

    static string FormatShare(double share)
    {
        double p = share * 100;
        if (p >= 99.995) return "100%";
        if (p < 0.01) return "0.01%未満";
        return p < 10 ? p.ToString("F2") + "%" : p.ToString("F1") + "%";
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
        if (playing) Tick();
        UpdateEffects();
        var mouse = Mouse.current;
        if (mouse == null)
        {
            map.Flush();
            return;
        }

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

        // 地図の上のクリック: マークがあれば拾う。無ければその国を選ぶ（海をクリックすると選ぶのをやめる）
        int select = selectId;
        if (mouse.leftButton.wasPressedThisFrame && inWindow)
        {
            if (!(playing && TryCollectBubble(mouse.position.ReadValue())) && onMap) select = hover;
        }

        if (hover != hoverId || select != selectId)
        {
            bool selectChanged = select != selectId;
            hoverId = hover;
            selectId = select;
            map.SetHighlight(hoverId, selectId);
            UpdateInfo();
            if (selectChanged) RefreshPanel();
        }
        map.Flush();
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
            infoText.text = playing
                ? "国をクリックで選ぶと様子が出て、右のパネルで種撒き・結婚ブーム\nホイールで拡大・縮小、右ドラッグで移動"
                : knownStart.Count > 0
                    ? "紫の国に最初から「" + Surname + "」さんがいます。右上のボタンで始めます\nホイールで拡大・縮小、右ドラッグで移動"
                    : "国をクリックして選び、右上の「この国から始める」を押してください\nホイールで拡大・縮小、右ドラッグで移動";
            return;
        }
        double people = spread != null && spread.Started ? spread.People(c.id) : 0;
        if (!playing && knownStart.TryGetValue(c.id, out var known)) people = known;
        infoText.text = c.name + "（" + c.continent + "）\n人口 " + WorldMap.FormatPeople(c.population)
                      + "\n「" + Surname + "」さん " + WorldMap.FormatPeople((long)people)
                      + (people >= 1 ? "（" + FormatShare(people / c.population) + "）" : "")
                      + (playing && spread.Campaigning(c.id) ? "　結婚ブーム中" : "")
                      + (playing && spread.HasLaw(c.id) ? "　名字変更禁止法あり" : "");
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
        if (map != null) spread = new NameSpread(map.Countries);

        var header = MakeBar(mapPanel.transform, "Header", true, 70, Background);
        titleText = MakeText(header, "", 30, Accent, new Vector2(30, 0), new Vector2(590, 60), new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        MakeButton(header, "名字を変える", new Vector2(-20, 0), new Vector2(200, 50), BackToEntry, new Vector2(1, 0.5f));
        dateText = MakeText(header, "", 24, TextColor, new Vector2(-500, 0), new Vector2(150, 60), new Vector2(1, 0.5f), TextAnchor.MiddleRight);

        // 速さの切りかえ（停止・普通・速い）
        speedButtons = new GameObject("SpeedButtons");
        Place(speedButtons, header, new Vector2(-240, 0), new Vector2(250, 50), new Vector2(1, 0.5f));
        speedImages = new Image[Speeds.Length];
        for (int k = 0; k < Speeds.Length; k++)
        {
            int index = k;
            var b = MakeButton(speedButtons.transform, SpeedNames[k], new Vector2(k * 85, 0), new Vector2(80, 44),
                               () => SetSpeed(index), new Vector2(0, 0.5f));
            speedImages[k] = b.GetComponent<Image>();
        }

        var footer = MakeBar(mapPanel.transform, "Footer", false, 110, Background);
        infoText = MakeText(footer, "", 24, TextColor, new Vector2(30, 0), new Vector2(760, 100), new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        worldText = MakeText(footer, "", 24, TextColor, new Vector2(-30, 0), new Vector2(440, 100), new Vector2(1, 0.5f), TextAnchor.MiddleRight);
        // 「始める」ボタンは、始める前だけ上の帯の速さボタンの場所に出す
        var sb = MakeButton(header, "この国から始める", new Vector2(-240, 0), new Vector2(250, 50), StartGame, new Vector2(1, 0.5f));
        startButton = sb.gameObject;
        startLabel = sb.GetComponentInChildren<Text>();
        if (spread != null) BuildPlayUI(mapGo.transform, area.transform, mapPanel.transform);
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

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 box, UnityEngine.Events.UnityAction onClick,
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
        t.fontSize = Mathf.Min(30, (int)(box.y * 0.55f));
        t.color = new Color(0.12f, 0.1f, 0.05f);
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.text = label;
        return b;
    }
}
