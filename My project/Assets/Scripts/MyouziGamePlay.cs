using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 名字ワールドの「遊ぶ」部分の画面（MyouziGame.cs の続き）:
//   ・ポイントのマーク（バズ）… 地図の上に出る紫の丸。クリックで知名度ポイント
//   ・広がりの線 ………………… 名字の人が新しい国に着いたとき、出発した国から線を引く
//   ・ニュース …………………… 地図の左上に、最近の出来事を出す
//   ・右の強化パネル ………… ポイントで強化を買う、選んだ国に名字の種を撒く・結婚ブーム、伝統を守る会のゲージ
//   ・勝ち・負けの画面
public partial class MyouziGame
{
    const float BubbleLife = 6f;     // マークが消えるまでの秒数（停止中は減らない）
    const float BubbleSize = 34f;
    const float LineLife = 2.5f;     // 線が消えるまでの秒数
    const int NewsLines = 3;
    const float PanelWidth = 300f;

    static readonly Color BubbleColor = new Color(0.67f, 0.2f, 0.8f);
    static readonly Color NeighborLineColor = new Color(0.55f, 0.15f, 0.7f);
    static readonly Color FlightLineColor = new Color(0.15f, 0.45f, 0.9f);
    static readonly Color PanelColor = new Color(0.12f, 0.17f, 0.29f);

    class Bubble
    {
        public RectTransform rt;
        public Vector2 uv;   // 地図の上の位置（左下 0,0 〜 右上 1,1）
        public float life;
    }

    class Line
    {
        public RectTransform rt;
        public Image img;
        public Vector2 from, to; // 地図の上の位置
        public float life;
        public Color color;
    }

    RectTransform overlay;          // 地図の上に重ねるもの（マーク・線）を入れる。地図と一緒に拡大・移動する
    readonly List<Bubble> bubbles = new List<Bubble>();
    readonly List<Line> lines = new List<Line>();
    readonly List<string> news = new List<string>();
    Text newsText;
    GameObject newsBox;
    Sprite circle;

    GameObject panel;
    Text pointsText, selectedText;
    Button campaignButton;
    Text campaignLabel;
    Button pushBackButton;
    Text pushBackLabel;
    Gauge alarmGauge, movementGauge;
    double lastAlarmNews, lastMovementNews; // ニュースを 1 回だけ出すため、前の日の値を覚えておく
    readonly List<(NameSpread.Upgrade u, Button b, Text t)> upgradeButtons = new List<(NameSpread.Upgrade, Button, Text)>();
    GameObject endScreen;
    Text endTitle, endBody;

    // ================= 1 日の出来事を画面に出す =================

    void HandleDayEvents()
    {
        foreach (var a in spread.Arrivals) ShowArrival(a);
        foreach (int id in spread.Bubbles) SpawnBubble(id);
        HandleOpposition();
    }

    void ShowArrival(NameSpread.Arrival a)
    {
        var to = map.Get(a.to);
        if (to == null) return;
        string how = a.kind == NameSpread.ArrivalKind.Neighbor ? "近い国から"
                   : a.kind == NameSpread.ArrivalKind.Flight ? "飛行機・船で"
                   : "種を撒いて";
        AddNews(DateLabel() + "　" + to.name + "に「" + Surname + "」さん上陸（" + how + "）");
        var from = map.Get(a.from);
        if (from != null)
            AddLine(LabelUv(from), LabelUv(to), a.kind == NameSpread.ArrivalKind.Flight ? FlightLineColor : NeighborLineColor);
    }

    void AddNews(string s)
    {
        news.Add(s);
        while (news.Count > NewsLines) news.RemoveAt(0);
        newsText.text = string.Join("\n", news);
        newsBox.SetActive(true);
    }

    // 国の名前の位置（地図の上の位置）
    Vector2 LabelUv(WorldMap.Country c) =>
        new Vector2((c.labelX + 0.5f) / map.Width, 1f - (c.labelY + 0.5f) / map.Height);

    // 地図の上の位置 → overlay の中の座標
    Vector2 UvToLocal(Vector2 uv)
    {
        var r = overlay.rect;
        return new Vector2(r.x + uv.x * r.width, r.y + uv.y * r.height);
    }

    // ================= マーク =================

    void SpawnBubble(int countryId)
    {
        var c = map.Get(countryId);
        if (c == null) return;
        var go = new GameObject("Bubble");
        go.transform.SetParent(overlay, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(BubbleSize, BubbleSize);
        var img = go.AddComponent<Image>();
        img.sprite = circle;
        img.color = BubbleColor;
        img.raycastTarget = false;
        var t = MakeText(go.transform, "バズ", 13, Color.white, Vector2.zero, new Vector2(BubbleSize, BubbleSize));
        t.fontStyle = FontStyle.Bold;
        // 国の名前の位置から少しずらす（同じ国に何個も出ても重ならないように）
        var jitter = new Vector2(Random.Range(-10f, 10f) / map.Width, Random.Range(-6f, 6f) / map.Height);
        bubbles.Add(new Bubble { rt = rt, uv = LabelUv(c) + jitter, life = BubbleLife });
    }

    // マウスの下にマークがあれば拾う
    bool TryCollectBubble(Vector2 screenPos)
    {
        for (int k = bubbles.Count - 1; k >= 0; k--)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(bubbles[k].rt, screenPos, null)) continue;
            Destroy(bubbles[k].rt.gameObject);
            bubbles.RemoveAt(k);
            spread.CollectBubble();
            RefreshPanel();
            return true;
        }
        return false;
    }

    // ================= 線 =================

    void AddLine(Vector2 from, Vector2 to, Color color)
    {
        var go = new GameObject("Line");
        go.transform.SetParent(overlay, false);
        go.transform.SetAsFirstSibling(); // マークより下に描く
        var rt = go.AddComponent<RectTransform>();
        rt.pivot = new Vector2(0f, 0.5f);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = color;
        lines.Add(new Line { rt = rt, img = img, from = from, to = to, life = LineLife, color = color });
    }

    // ================= 毎フレーム =================

    void UpdateEffects()
    {
        float scale = 1f / zoom; // 地図を拡大しても、マークと線の太さは同じ大きさに見せる
        bool running = playing && Speeds[speedIndex] > 0;

        for (int k = bubbles.Count - 1; k >= 0; k--)
        {
            var b = bubbles[k];
            if (running) b.life -= Time.deltaTime;
            if (b.life <= 0)
            {
                Destroy(b.rt.gameObject);
                bubbles.RemoveAt(k);
                continue;
            }
            b.rt.anchoredPosition = UvToLocal(b.uv);
            // 消える前の 1.5 秒は点滅する
            float pulse = b.life < 1.5f ? (Mathf.Sin(b.life * 20f) > 0 ? 1f : 0.4f) : 1f;
            b.rt.localScale = new Vector3(scale * pulse, scale * pulse, 1f);
        }

        for (int k = lines.Count - 1; k >= 0; k--)
        {
            var l = lines[k];
            l.life -= Time.deltaTime;
            if (l.life <= 0)
            {
                Destroy(l.rt.gameObject);
                lines.RemoveAt(k);
                continue;
            }
            var a = UvToLocal(l.from);
            var d = UvToLocal(l.to) - a;
            l.rt.anchoredPosition = a;
            l.rt.sizeDelta = new Vector2(d.magnitude, 3f * scale);
            l.rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var c = l.color;
            c.a = Mathf.Clamp01(l.life / LineLife * 1.5f);
            l.img.color = c;
        }
    }

    void ClearEffects()
    {
        foreach (var b in bubbles) Destroy(b.rt.gameObject);
        bubbles.Clear();
        foreach (var l in lines) Destroy(l.rt.gameObject);
        lines.Clear();
        news.Clear();
        lastAlarmNews = lastMovementNews = 0;
        if (newsText != null) newsText.text = "";
        if (newsBox != null) newsBox.SetActive(false);
    }

    // ================= 右の強化パネル =================

    // 遊んでいる間だけ右にパネルを出し、地図はその左に縮める
    void SetPlayingLayout(bool on)
    {
        if (panel != null) panel.SetActive(on);
        if (endScreen != null) endScreen.SetActive(false);
        areaRect.offsetMax = new Vector2(on ? -(PanelWidth + 10) : -10, -70);
        ResetView();
    }

    void BuyUpgrade(NameSpread.Upgrade u)
    {
        if (spread.Buy(u))
        {
            AddNews(spread.Day + "日目　「" + u.name + "」を強化（レベル" + u.level + "）");
            RefreshPanel();
        }
    }

    void CampaignSelected()
    {
        var c = map.Get(selectId);
        if (c == null) return;
        bool seeded = spread.People(selectId) < 1; // 名字の人がまだいない国なら「種を撒く」
        if (spread.Campaign(selectId))
        {
            AddNews(DateLabel() + "　" + c.name + (seeded ? "に「" + Surname + "」の種を撒いた" : "で「" + Surname + "」さんの結婚ブーム！"));
            foreach (var a in spread.Arrivals) if (a.kind == NameSpread.ArrivalKind.Campaign && a.to == selectId) ShowArrival(a);
            RefreshColors();
            RefreshTexts();
            RefreshPanel();
        }
    }

    void PushBack()
    {
        if (spread.PushBack())
        {
            AddNews(spread.Day + "日目　元に戻そう運動を押し戻した！（" + FormatPercent(spread.Movement) + "）");
            RefreshPanel();
        }
    }

    // 伝統を守る会の動きをニュースにする（1 日ごとに呼ぶ）
    void HandleOpposition()
    {
        if (spread.Alarm >= 10 && lastAlarmNews < 10) AddNews(spread.Day + "日目　伝統を守る会が「" + Surname + "」に気づいた");
        if (spread.MovementBegan) AddNews(spread.Day + "日目　伝統を守る会が「元に戻そう運動」を始めた！");
        if (spread.Alarm >= NameSpread.LawStart && lastAlarmNews < NameSpread.LawStart)
            AddNews(spread.Day + "日目　警戒度" + (int)NameSpread.LawStart + "。名字変更禁止法の動きが出てきた");
        lastAlarmNews = spread.Alarm;
        foreach (int id in spread.NewLaws)
        {
            var c = map.Get(id);
            if (c != null) AddNews(DateLabel() + "　" + c.name + "で名字変更禁止法が成立…");
        }
        if (spread.Movement >= 80 && lastMovementNews < 80) AddNews(spread.Day + "日目　元に戻そう運動が80%！ 押し戻さないと負ける");
        lastMovementNews = spread.Movement;
        if (spread.NewLaws.Count > 0) RefreshColors();
        if (spread.Over) ShowEnd();
    }

    static string FormatPercent(double p) => p.ToString("F0") + "%";

    // 始めてからの時間（1 年 = 365 日、1 か月 = 30 日として数える）。例: 「2年4か月」
    string DateLabel()
    {
        int d = Mathf.Max(0, spread.Day - 1);
        int years = d / 365;
        int months = d % 365 / 30;
        return years == 0 ? months + "か月" : years + "年" + months + "か月";
    }

    void RefreshPanel()
    {
        if (spread == null || panel == null || !panel.activeSelf) return;
        pointsText.text = "知名度ポイント " + spread.Points;
        SetGauge(alarmGauge, (float)(spread.Alarm / 100), "伝統を守る会の警戒度 " + FormatPercent(spread.Alarm));
        SetGauge(movementGauge, (float)(spread.Movement / 100), "元に戻そう運動 " + FormatPercent(spread.Movement));

        pushBackButton.interactable = spread.CanPushBack;
        pushBackLabel.text = "運動を押し戻す（" + NameSpread.PushBackCost + "pt で -" + NameSpread.PushBackAmount + "%）";

        var c = map.Get(selectId);
        if (c == null)
        {
            selectedText.text = "国をクリックで選ぶと、種撒き・結婚ブーム";
            campaignButton.interactable = false;
            campaignLabel.text = "名字の種を撒く／結婚ブーム";
        }
        else
        {
            selectedText.text = "選んだ国: " + c.name + (spread.HasLaw(selectId) ? "（禁止法あり）" : "");
            if (spread.Campaigning(selectId))
            {
                campaignButton.interactable = false;
                campaignLabel.text = "結婚ブーム中";
            }
            else
            {
                campaignButton.interactable = spread.CanCampaign(selectId);
                campaignLabel.text = (spread.People(selectId) < 1 ? "この国に名字の種を撒く" : "この国で結婚ブーム")
                                   + "（" + NameSpread.CampaignCost + "pt）";
            }
        }

        foreach (var ub in upgradeButtons)
        {
            var u = ub.u;
            ub.b.interactable = spread.CanBuy(u);
            string level = "Lv" + u.level + "/" + u.MaxLevel;
            string cost = u.IsMax ? "MAX" : u.NextCost + "pt";
            ub.t.text = u.name + "　" + level + "　" + cost + "\n" + u.help;
        }
    }

    // ゲージ（横長の棒）。value は 0〜1
    class Gauge
    {
        public RectTransform fill;
        public Text label;
    }

    Gauge MakeGauge(Transform parent, float y, Color color)
    {
        var bg = new GameObject("Gauge");
        Place(bg, parent, new Vector2(0, y), new Vector2(280, 24), new Vector2(0.5f, 1f));
        bg.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f);
        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(bg.transform, false);
        var frt = fillGo.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0, 1);
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        var img = fillGo.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var label = MakeText(bg.transform, "", 15, TextColor, new Vector2(8, 0), new Vector2(270, 24), new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        return new Gauge { fill = frt, label = label };
    }

    static void SetGauge(Gauge g, float value, string text)
    {
        g.fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
        g.label.text = text;
    }

    // ================= 勝ち・負けの画面 =================

    void ShowEnd()
    {
        SetSpeed(0);
        bool win = spread.Result == NameSpread.Outcome.Win;
        string stats = "\n\nかかった時間 " + DateLabel() + "　" + spread.CountriesReached + "か国　「" + Surname + "」さん "
                     + WorldMap.FormatPeople((long)spread.Total) + "（世界の " + FormatShare(spread.WorldShare) + "）";
        endTitle.text = win ? "世界制覇！" : "元に戻そう運動が成功…";
        endTitle.color = win ? Accent : new Color(1f, 0.55f, 0.5f);
        endBody.text = win
            ? "世界の全員が「" + Surname + "」さんになった！" + stats + "\n\nランク " + spread.Rank()
              + "（日数が少ないほど、名字が珍しいほど高い）"
            : "世界は元の名字に戻ってしまった。" + stats + "\n\n「反発をおさえる」の強化や「運動を押し戻す」を使ってみよう";
        endScreen.SetActive(true);
        endScreen.transform.SetAsLastSibling();
        AddNews(DateLabel() + "　" + (win ? "世界制覇！" : "負け…元に戻そう運動が成功した"));
        RefreshPanel();
    }

    void BuildEndScreen(Transform mapPanelTransform)
    {
        endScreen = MakePanel(mapPanelTransform, "EndScreen");
        endScreen.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f); // 後ろを暗くして、クリックも止める
        var box = new GameObject("Box");
        Place(box, endScreen.transform, Vector2.zero, new Vector2(760, 420));
        box.AddComponent<Image>().color = Background;
        endTitle = MakeText(box.transform, "", 56, Accent, new Vector2(0, 140), new Vector2(720, 80));
        endBody = MakeText(box.transform, "", 24, TextColor, new Vector2(0, 0), new Vector2(700, 200));
        MakeButton(box.transform, "もう一度", new Vector2(-150, -160), new Vector2(240, 60), OpenMap);
        MakeButton(box.transform, "名字を変える", new Vector2(150, -160), new Vector2(240, 60), BackToEntry);
        endScreen.SetActive(false);
    }

    void BuildPlayUI(Transform mapTransform, Transform areaTransform, Transform mapPanelTransform)
    {
        circle = MakeCircleSprite(64);

        overlay = MakePanel(mapTransform, "Overlay").GetComponent<RectTransform>();

        // ニュース（地図の窓の左上。地図と一緒には動かない）
        newsBox = new GameObject("News");
        Place(newsBox, areaTransform, new Vector2(6, -6), new Vector2(560, 72), new Vector2(0, 1));
        newsBox.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.8f);
        newsText = MakeText(newsBox.transform, "", 17, new Color(0.15f, 0.12f, 0.2f), new Vector2(8, 0), new Vector2(544, 68),
                            new Vector2(0, 0.5f), TextAnchor.MiddleLeft);
        newsBox.SetActive(false);

        // 右の強化パネル（上の帯と下の帯の間）
        panel = new GameObject("UpgradePanel");
        panel.transform.SetParent(mapPanelTransform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(1, 0);
        prt.anchorMax = new Vector2(1, 1);
        prt.pivot = new Vector2(1, 0.5f);
        prt.offsetMin = new Vector2(-PanelWidth, 110);
        prt.offsetMax = new Vector2(0, -70);
        panel.AddComponent<Image>().color = PanelColor;

        // 上の部分（動かない）: ポイントと 2 本のゲージ
        var top = new Vector2(0.5f, 1f);
        pointsText = MakeText(panel.transform, "", 24, Accent, new Vector2(0, -4), new Vector2(280, 32), top);
        alarmGauge = MakeGauge(panel.transform, -38, new Color(0.85f, 0.55f, 0.15f));
        movementGauge = MakeGauge(panel.transform, -66, new Color(0.85f, 0.2f, 0.2f));
        const float fixedHeight = 96;

        // 下の部分（ホイールでスクロールできる）: 種撒き・結婚ブーム、押し戻す、強化の一覧
        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(panel.transform, false);
        var srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(0, 4);
        srt.offsetMax = new Vector2(0, -fixedHeight);
        scrollGo.AddComponent<Image>().color = new Color(0, 0, 0, 0); // ホイールを受け取るため（見えない）
        scrollGo.AddComponent<RectMask2D>();
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        scroll.viewport = srt;

        var content = new GameObject("Content");
        content.transform.SetParent(scrollGo.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        scroll.content = crt;

        float y = 0;
        selectedText = MakeText(content.transform, "", 16, TextColor, new Vector2(0, y), new Vector2(280, 24), top);
        y -= 26;
        campaignButton = MakeButton(content.transform, "", new Vector2(0, y), new Vector2(280, 40), CampaignSelected, top);
        campaignLabel = campaignButton.GetComponentInChildren<Text>();
        campaignLabel.fontSize = 18;
        y -= 44;
        pushBackButton = MakeButton(content.transform, "", new Vector2(0, y), new Vector2(280, 40), PushBack, top);
        pushBackLabel = pushBackButton.GetComponentInChildren<Text>();
        pushBackLabel.fontSize = 16;
        y -= 48;

        string group = null;
        foreach (var u in spread.Upgrades)
        {
            if (u.group != group)
            {
                group = u.group;
                MakeText(content.transform, "― " + group + " ―", 16, Accent, new Vector2(0, y), new Vector2(280, 22), top);
                y -= 24;
            }
            var upgrade = u;
            var b = MakeButton(content.transform, "", new Vector2(0, y), new Vector2(280, 46), () => BuyUpgrade(upgrade), top);
            var t = b.GetComponentInChildren<Text>();
            t.fontSize = 15;
            t.alignment = TextAnchor.MiddleLeft;
            var trt = t.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(10, 0);
            trt.offsetMax = new Vector2(-6, 0);
            upgradeButtons.Add((u, b, t));
            y -= 50;
        }
        crt.sizeDelta = new Vector2(0, -y + 4);
        panel.SetActive(false);

        BuildEndScreen(mapPanelTransform);
    }

    static Sprite MakeCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255); // ふちをなめらかに
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
