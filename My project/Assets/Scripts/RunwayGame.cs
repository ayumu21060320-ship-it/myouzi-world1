using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// ランウェイ・ウォーク（仮）: パリコレモデルの足を操作して、ランウェイの先まできれいに歩くバカゲー。
// シーンを再生すると自動で起動する（シーンに置く必要なし）。モデルの体は RunwayModel.cs。
//
// 操作: A / D = 左足を前へ / 後ろへ、J / L = 右足を前へ / 後ろへ、スペース = ひざを伸ばす、R = やり直し
// ランウェイの先の「POSE」の場所で、2 秒まっすぐ立てたらクリア（決めポーズ）。
// 頭か胴体が床に着いたら転倒（スローモーションになり、カメラのフラッシュが光る）。
public class RunwayGame : MonoBehaviour
{
    // ---- 調整用の数 ----
    const float RunwayLength = 12f;    // スタートから POSE の場所の終わりまで（メートル）
    const float PoseZone = 2.5f;       // ランウェイの先の、ポーズを決める場所の長さ
    const float PoseSeconds = 2f;      // ポーズの場所で、まっすぐ立っていないといけない時間
    const float PoseMaxTilt = 15f;     // まっすぐとみなす体の傾き（度）
    const int Substeps = 4;            // 物理の 1 回（0.01 秒）を何回に分けて計算するか（ふるえを防ぐ）
    const float SlowMotion = 0.25f;    // 転んだときの速さ
    const float FallShowSeconds = 2f;  // 転んでから結果が出るまで（実際の秒）

    static readonly Color Back = new Color(0.07f, 0.06f, 0.1f);
    static readonly Color RunwayColor = new Color(0.92f, 0.92f, 0.95f);
    static readonly Color Accent = new Color(1f, 0.85f, 0.4f);
    static readonly Color TextColor = new Color(0.95f, 0.95f, 0.97f);

    enum State { Ready, Walking, Falling, Result }
    State state;

    RunwayModel model;
    Sprite square, circle;
    PhysicsMaterial2D grip, slip;
    Collider2D floor;
    Camera cam;
    Font font;
    Text statusText, centerText, resultText;
    GameObject resultPanel;
    float elapsed, poseTimer, fallTimer, eleganceSum;
    int eleganceSamples;
    bool cleared;
    float flashTimer;
    SpriteRenderer flash;   // 画面全体を白く光らせる（カメラのフラッシュ）
    Transform audience;     // 観客（手前の黒い頭の列）

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<RunwayGame>() == null) new GameObject("RunwayGame").AddComponent<RunwayGame>();
    }

    void Start()
    {
        // 物理は自分で少しずつ進める（スタートするまで止めておけるように、ふるえないように細かく）
        Physics2D.simulationMode = SimulationMode2D.Script;
        Time.fixedDeltaTime = 0.01f;
        RunwayModel.StepTime = Time.fixedDeltaTime / Substeps;

        square = MakeSquareSprite();
        circle = MakeCircleSprite(64);
        grip = new PhysicsMaterial2D("Grip") { friction = 1.2f, bounciness = 0f };
        slip = new PhysicsMaterial2D("Slip") { friction = 0.3f, bounciness = 0f };

        cam = Camera.main;
        if (cam == null) cam = new GameObject("Camera").AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Back;

        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0]
             : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Hiragino Sans" }, 64);

        BuildStage();
        BuildUI();
        ResetGame();
    }

    // ================= 進み =================

    void ResetGame()
    {
        if (model != null) model.Destroy();
        model = new RunwayModel(Vector2.zero, square, circle, grip, slip);
        state = State.Ready;
        elapsed = poseTimer = fallTimer = eleganceSum = 0f;
        eleganceSamples = 0;
        cleared = false;
        Time.timeScale = 1f;
        resultPanel.SetActive(false);
        centerText.text = "キーを押すとスタート\n\nA / D … 左足を 前へ / 後ろへ\nJ / L … 右足を 前へ / 後ろへ\nスペース … ひざを伸ばす\n\nランウェイの先の POSE で、2 秒きれいに立てばクリア";
        PlaceCamera(true);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.rKey.wasPressedThisFrame)
        {
            ResetGame();
            return;
        }

        model.LeftForward = kb.aKey.isPressed;
        model.LeftBack = kb.dKey.isPressed;
        model.RightForward = kb.jKey.isPressed;
        model.RightBack = kb.lKey.isPressed;
        model.KneesStraight = kb.spaceKey.isPressed;

        if (state == State.Ready && (kb.aKey.isPressed || kb.dKey.isPressed || kb.jKey.isPressed || kb.lKey.isPressed || kb.spaceKey.isPressed))
        {
            state = State.Walking;
            centerText.text = "";
        }

        if (state == State.Walking) elapsed += Time.deltaTime;

        if (state == State.Falling)
        {
            fallTimer += Time.unscaledDeltaTime;
            if (fallTimer >= FallShowSeconds) ShowResult();
        }

        UpdateFlash();
        UpdateStatus();
    }

    void FixedUpdate()
    {
        if (state == State.Ready || state == State.Result) return;
        float h = Time.fixedDeltaTime / Substeps;
        for (int k = 0; k < Substeps; k++)
        {
            if (state == State.Walking) model.ApplyControls();
            Physics2D.Simulate(h);
        }
        if (state == State.Walking) CheckWalking();
    }

    void LateUpdate()
    {
        if (model != null) PlaceCamera(false);
    }

    void CheckWalking()
    {
        // 転んだ: 頭か胴体が床に着いた、またはランウェイから落ちた
        if (model.HeadCollider.IsTouching(floor) || model.TorsoCollider.IsTouching(floor) || model.HeadHeight < 0.3f)
        {
            state = State.Falling;
            Time.timeScale = SlowMotion;
            fallTimer = 0f;
            Flash();
            centerText.text = "転倒…";
            return;
        }

        // エレガント度: 体がまっすぐで、頭が高いほど高い（0〜100）
        float standHead = 1.75f;
        float e = 100f - Mathf.Abs(model.Tilt) * 2f - Mathf.Max(0f, standHead - model.HeadHeight) * 150f;
        eleganceSum += Mathf.Clamp(e, 0f, 100f);
        eleganceSamples++;

        // ポーズの場所でまっすぐ立っている
        bool inZone = model.X >= RunwayLength - PoseZone && model.X <= RunwayLength;
        if (inZone && Mathf.Abs(model.Tilt) < PoseMaxTilt && model.HeadHeight > 1.5f)
        {
            poseTimer += Time.fixedDeltaTime;
            centerText.text = "ポーズ中… " + poseTimer.ToString("F1") + " 秒";
            if (poseTimer >= PoseSeconds)
            {
                cleared = true;
                Flash();
                ShowResult();
            }
        }
        else if (poseTimer > 0f)
        {
            poseTimer = 0f;
            centerText.text = "";
        }

        // ときどき観客のカメラが光る
        if (Random.value < 0.01f) Flash(0.25f);
    }

    float Elegance => eleganceSamples > 0 ? eleganceSum / eleganceSamples : 0f;

    void ShowResult()
    {
        state = State.Result;
        Time.timeScale = 1f;
        centerText.text = "";
        float distance = Mathf.Clamp(model.X, 0f, RunwayLength);
        string title;
        if (cleared)
            title = Elegance >= 80 ? "伝説のスーパーモデル" : Elegance >= 60 ? "トップモデル" : "ガニ股モデル";
        else
            title = distance < 2f ? "生まれたての子鹿" : distance < 8f ? "酔っぱらいのペンギン" : distance < RunwayLength - PoseZone ? "期待の新人モデル" : "あと一歩のモデル";
        resultText.text = (cleared ? "決めポーズ！ クリア！" : "転倒…") + "\n\n「" + title + "」\n\n"
                        + "歩いた距離 " + distance.ToString("F1") + " m / " + RunwayLength.ToString("F0") + " m\n"
                        + "時間 " + elapsed.ToString("F1") + " 秒\n"
                        + "エレガント度 " + Elegance.ToString("F0") + " / 100";
        resultPanel.SetActive(true);
    }

    // ================= 見た目 =================

    void PlaceCamera(bool snap)
    {
        float x = Mathf.Clamp(model.X + 1.2f, 0.5f, RunwayLength + 1f);
        var target = new Vector3(x, 1.2f, -10f);
        cam.transform.position = snap ? target : Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        if (audience != null) audience.position = new Vector3(cam.transform.position.x * 0.3f, 0f, 0f); // 手前の観客はゆっくり動く（奥行きに見せる）
        if (flash != null) flash.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
    }

    void Flash(float strength = 0.8f)
    {
        flashTimer = Mathf.Max(flashTimer, strength);
    }

    void UpdateFlash()
    {
        flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime * 3f);
        flash.color = new Color(1f, 1f, 1f, flashTimer);
    }

    void UpdateStatus()
    {
        float distance = Mathf.Clamp(model.X, 0f, RunwayLength);
        statusText.text = "距離 " + distance.ToString("F1") + " / " + RunwayLength.ToString("F0") + " m　　"
                        + "時間 " + elapsed.ToString("F1") + " 秒　　"
                        + "エレガント度 " + Elegance.ToString("F0");
    }

    void BuildStage()
    {
        // 床（ランウェイ）。POSE の場所の先で終わっていて、歩きすぎると落ちる
        var floorGo = new GameObject("Runway");
        floorGo.transform.position = new Vector3((RunwayLength - 4f) / 2f, -0.5f, 0f);
        var box = floorGo.AddComponent<BoxCollider2D>();
        box.size = new Vector2(RunwayLength + 4f, 1f);
        box.sharedMaterial = grip;
        floor = box;
        RunwayModel.AddVisual(floorGo.transform, square, RunwayColor, Vector2.zero, box.size, -5);
        RunwayModel.AddVisual(floorGo.transform, square, new Color(0.75f, 0.1f, 0.2f), new Vector2(0, 0.48f), new Vector2(box.size.x, 0.04f), -4); // 赤いふち

        // POSE の場所（金色の床）と文字の代わりの目印
        RunwayModel.AddVisual(floorGo.transform, square, Accent,
            new Vector2(RunwayLength - PoseZone / 2f - floorGo.transform.position.x, 0.48f), new Vector2(PoseZone, 0.05f), -3);
        for (int k = 0; k <= 4; k++)
        {
            var lamp = RunwayModel.AddVisual(null, circle, new Color(1f, 0.9f, 0.5f, 0.6f), Vector2.zero, new Vector2(0.18f, 0.18f), -6);
            lamp.transform.position = new Vector3(RunwayLength - PoseZone + k * PoseZone / 4f, 3.0f, 0f);
        }

        // 1 m ごとの目盛り
        for (int m = 0; m <= (int)RunwayLength; m++)
        {
            var tick = RunwayModel.AddVisual(null, square, new Color(0.6f, 0.6f, 0.65f), Vector2.zero, new Vector2(0.02f, 0.1f), -4);
            tick.transform.position = new Vector3(m, -0.06f, 0f);
        }

        // 奥の観客（ぼんやりした頭）と、手前の観客（黒い頭の列）
        for (int k = 0; k < 80; k++)
        {
            float x = -6f + k * 0.45f + Random.Range(-0.1f, 0.1f);
            var head = RunwayModel.AddVisual(null, circle, new Color(0.18f, 0.16f, 0.24f), Vector2.zero, new Vector2(0.28f, 0.28f), -8);
            head.transform.position = new Vector3(x, 2.1f + Random.Range(-0.05f, 0.08f), 0f);
        }
        audience = new GameObject("Audience").transform;
        for (int k = 0; k < 60; k++)
        {
            var head = RunwayModel.AddVisual(audience, circle, new Color(0.02f, 0.02f, 0.03f), Vector2.zero, new Vector2(0.5f, 0.5f), 20);
            head.transform.localPosition = new Vector3(-6f + k * 0.55f + Random.Range(-0.1f, 0.1f), -0.95f + Random.Range(-0.05f, 0.08f), 0f);
        }

        // カメラのフラッシュ（画面全体を覆う白い四角。ふだんは透明）
        flash = RunwayModel.AddVisual(null, square, new Color(1, 1, 1, 0), Vector2.zero, new Vector2(40f, 20f), 100);
    }

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

        statusText = MakeText(canvasGo.transform, 26, TextColor, new Vector2(0, -30), new Vector2(1200, 40), new Vector2(0.5f, 1f));
        MakeText(canvasGo.transform, 20, new Color(0.7f, 0.7f, 0.75f), new Vector2(0, 24), new Vector2(1200, 30), new Vector2(0.5f, 0f))
            .text = "A / D 左足　　J / L 右足　　スペース ひざを伸ばす　　R やり直し";
        centerText = MakeText(canvasGo.transform, 30, Accent, new Vector2(0, 120), new Vector2(1100, 360), new Vector2(0.5f, 0.5f));

        resultPanel = new GameObject("Result");
        resultPanel.transform.SetParent(canvasGo.transform, false);
        var rt = resultPanel.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(640, 420);
        resultPanel.AddComponent<Image>().color = new Color(0.1f, 0.08f, 0.15f, 0.92f);
        resultText = MakeText(resultPanel.transform, 30, TextColor, new Vector2(0, 40), new Vector2(600, 320), new Vector2(0.5f, 0.5f));
        var button = new GameObject("Retry");
        button.transform.SetParent(resultPanel.transform, false);
        var brt = button.AddComponent<RectTransform>();
        brt.anchoredPosition = new Vector2(0, -160);
        brt.sizeDelta = new Vector2(280, 60);
        var img = button.AddComponent<Image>();
        img.color = Accent;
        var b = button.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(ResetGame);
        var label = MakeText(button.transform, 28, new Color(0.1f, 0.08f, 0.1f), Vector2.zero, new Vector2(280, 60), new Vector2(0.5f, 0.5f));
        label.text = "もう一度（R）";
        resultPanel.SetActive(false);
    }

    Text MakeText(Transform parent, int size, Color color, Vector2 pos, Vector2 box, Vector2 anchor)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Sprite MakeSquareSprite()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // 1 × 1 の大きさ
    }

    static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); // 直径 1
    }
}
