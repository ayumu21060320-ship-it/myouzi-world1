using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 名字ワールドの「遊ぶ」部分の画面（MyouziGame.cs の続き）:
//   ・ポイントのマーク（バズ）… 地図の上に出る紫の丸。クリックで知名度ポイント
//   ・広がりの線 ………………… 名字の人が新しい国に着いたとき、出発した国から線を引く
//   ・ニュース …………………… 地図の左上に、最近の出来事を出す
//   ・右の強化パネル ………… ポイントで強化を買う、選んだ国で宣伝する
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
    readonly List<(NameSpread.Upgrade u, Button b, Text t)> upgradeButtons = new List<(NameSpread.Upgrade, Button, Text)>();

    // ================= 1 日の出来事を画面に出す =================

    void HandleDayEvents()
    {
        foreach (var a in spread.Arrivals) ShowArrival(a);
        foreach (int id in spread.Bubbles) SpawnBubble(id);
        if (spread.Arrivals.Count > 0) RefreshPanel();
    }

    void ShowArrival(NameSpread.Arrival a)
    {
        var to = map.Get(a.to);
        if (to == null) return;
        string how = a.kind == NameSpread.ArrivalKind.Neighbor ? "近い国から"
                   : a.kind == NameSpread.ArrivalKind.Flight ? "飛行機・船で"
                   : "宣伝で";
        AddNews(spread.Day + "日目　" + to.name + "に「" + Surname + "」さん上陸（" + how + "）");
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
        if (newsText != null) newsText.text = "";
        if (newsBox != null) newsBox.SetActive(false);
    }

    // ================= 右の強化パネル =================

    // 遊んでいる間だけ右にパネルを出し、地図はその左に縮める
    void SetPlayingLayout(bool on)
    {
        if (panel != null) panel.SetActive(on);
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
        if (spread.Campaign(selectId))
        {
            AddNews(spread.Day + "日目　" + c.name + "で「" + Surname + "」を大宣伝！");
            foreach (var a in spread.Arrivals) if (a.kind == NameSpread.ArrivalKind.Campaign && a.to == selectId) ShowArrival(a);
            RefreshColors();
            RefreshTexts();
            RefreshPanel();
        }
    }

    void RefreshPanel()
    {
        if (spread == null || panel == null || !panel.activeSelf) return;
        pointsText.text = "知名度ポイント " + spread.Points;

        var c = map.Get(selectId);
        if (c == null)
        {
            selectedText.text = "国をクリックで選ぶと宣伝できます";
            campaignButton.interactable = false;
            campaignLabel.text = "この国で宣伝する";
        }
        else
        {
            selectedText.text = "選んだ国: " + c.name;
            if (spread.Campaigning(selectId))
            {
                campaignButton.interactable = false;
                campaignLabel.text = "宣伝中";
            }
            else
            {
                campaignButton.interactable = spread.CanCampaign(selectId);
                campaignLabel.text = (spread.People(selectId) < 1 ? "この国に送り込む" : "この国で宣伝する")
                                   + "（" + NameSpread.CampaignCost + "pt）";
            }
        }

        foreach (var ub in upgradeButtons)
        {
            var u = ub.u;
            var t = ub.t;
            var b = ub.b;
            b.interactable = spread.CanBuy(u);
            string level = "Lv" + u.level + "/" + u.MaxLevel;
            string cost = u.IsMax ? "MAX" : u.NextCost + "pt";
            t.text = u.name + "　" + level + "　" + cost + "\n" + u.help;
        }
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

        var top = new Vector2(0.5f, 1f);
        float y = -6;
        pointsText = MakeText(panel.transform, "", 26, Accent, new Vector2(0, y), new Vector2(280, 36), top);
        y -= 38;
        selectedText = MakeText(panel.transform, "", 16, TextColor, new Vector2(0, y), new Vector2(280, 24), top);
        y -= 26;
        campaignButton = MakeButton(panel.transform, "", new Vector2(0, y), new Vector2(280, 40), CampaignSelected, top);
        campaignLabel = campaignButton.GetComponentInChildren<Text>();
        campaignLabel.fontSize = 18;
        y -= 48;

        string group = null;
        foreach (var u in spread.Upgrades)
        {
            if (u.group != group)
            {
                group = u.group;
                MakeText(panel.transform, "― " + group + " ―", 16, Accent, new Vector2(0, y), new Vector2(280, 22), top);
                y -= 24;
            }
            var upgrade = u;
            var b = MakeButton(panel.transform, "", new Vector2(0, y), new Vector2(280, 46), () => BuyUpgrade(upgrade), top);
            var t = b.GetComponentInChildren<Text>();
            t.fontSize = 15;
            t.alignment = TextAnchor.MiddleLeft;
            var trt = t.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(10, 0);
            trt.offsetMax = new Vector2(-6, 0);
            upgradeButtons.Add((u, b, t));
            y -= 50;
        }
        panel.SetActive(false);
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
