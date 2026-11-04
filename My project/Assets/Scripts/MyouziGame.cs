using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 名字ワールドの画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）。
// いまは最初の「名字を入力する画面」だけ。
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
    GameObject donePanel;
    Text doneText;

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

        doneText.text = "世界に「" + name + "」を広めよう！\n\n目標：世界の全員を「" + name + "」さんにする\n\n（この先のゲームはこれから作ります）";
        entryPanel.SetActive(false);
        donePanel.SetActive(true);
    }

    void BackToEntry()
    {
        donePanel.SetActive(false);
        entryPanel.SetActive(true);
        nameInput.ActivateInputField();
    }

    void ShowMessage(string msg)
    {
        messageText.text = msg;
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
        var canvas = canvasGo.AddComponent<Canvas>();
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

        // ---- 決めたあとの画面（仮）----
        donePanel = MakePanel(canvasGo.transform, "DonePanel");
        doneText = MakeText(donePanel.transform, "", 36, TextColor, new Vector2(0, 60), new Vector2(1100, 300));
        MakeButton(donePanel.transform, "名字を変える", new Vector2(0, -170), new Vector2(280, 64), BackToEntry);
        donePanel.SetActive(false);
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
    RectTransform Place(GameObject go, Transform parent, Vector2 pos, Vector2 size)
    {
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    Text MakeText(Transform parent, string s, int size, Color color, Vector2 pos, Vector2 box)
    {
        var go = new GameObject("Text");
        Place(go, parent, pos, box);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
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

    void MakeButton(Transform parent, string label, Vector2 pos, Vector2 box, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button");
        Place(go, parent, pos, box);
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
