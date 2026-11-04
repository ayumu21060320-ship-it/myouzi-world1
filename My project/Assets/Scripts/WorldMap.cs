using System;
using UnityEngine;

// 世界地図のデータと絵。Unity の画面には依存しない（絵は Texture2D として作るだけ）。
// データは tools/make_world_map.py で作った Assets/Resources/Map/ の 2 つのファイル:
//   CountryMap.bytes … 1 ピクセルごとに国の番号（0 = 海、255 = 人の住まない土地）。上の行から順
//   Countries.json   … 国の一覧
public class WorldMap
{
    [Serializable]
    public class Country
    {
        public int id;
        public string name;      // 日本語の名前
        public string nameEn;
        public long population;
        public string continent;
        public int labelX, labelY; // 名前を出す位置（地図のピクセル。上が 0）
    }

    [Serializable]
    class CountryList
    {
        public int width, height;
        public Country[] countries;
    }

    public const byte Ocean = 0;
    public const byte NoPeople = 255;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Texture2D Texture { get; private set; }
    public Country[] Countries { get; private set; }
    public long WorldPopulation { get; private set; }

    // 地図の色（参考: 黄緑の陸、緑の国境、白い海）
    public static readonly Color32 SeaColor = new Color32(255, 255, 255, 255);
    public static readonly Color32 LandColor = new Color32(190, 210, 60, 255);
    public static readonly Color32 EmptyLandColor = new Color32(215, 225, 170, 255);
    public static readonly Color32 LineColor = new Color32(70, 150, 40, 255);
    public static readonly Color32 HoverColor = new Color32(225, 240, 110, 255);
    public static readonly Color32 SelectColor = new Color32(255, 170, 60, 255);

    byte[] ids;          // ピクセルごとの国の番号（Texture と同じく下の行から順）
    bool[] edge;         // 国境・海岸線のピクセル
    Color32[] pixels;
    Country[] byId = new Country[256];
    int[] pixStart, pixList; // 国ごとのピクセルの一覧（番号 id の国は pixList[pixStart[id] .. pixStart[id+1]-1]）
    int hoverId, selectId;

    public static WorldMap Load()
    {
        var json = Resources.Load<TextAsset>("Map/Countries");
        var bytes = Resources.Load<TextAsset>("Map/CountryMap");
        if (json == null || bytes == null)
        {
            Debug.LogError("Assets/Resources/Map/ に Countries.json と CountryMap.bytes がありません");
            return null;
        }
        var list = JsonUtility.FromJson<CountryList>(json.text);
        var m = new WorldMap();
        m.Build(list, bytes.bytes);
        return m;
    }

    void Build(CountryList list, byte[] raw)
    {
        Width = list.width;
        Height = list.height;
        Countries = list.countries;
        foreach (var c in Countries)
        {
            byId[c.id] = c;
            WorldPopulation += c.population;
        }

        // ファイルは上の行から、Texture2D は下の行から並ぶので、上下を入れかえる
        int n = Width * Height;
        ids = new byte[n];
        for (int y = 0; y < Height; y++)
            Buffer.BlockCopy(raw, (Height - 1 - y) * Width, ids, y * Width, Width);

        edge = new bool[n];
        var counts = new int[257];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                byte v = ids[i];
                counts[v + 1]++;
                if (v == Ocean) continue;
                edge[i] = (x + 1 < Width && ids[i + 1] != v) || (x > 0 && ids[i - 1] != v)
                       || (y + 1 < Height && ids[i + Width] != v) || (y > 0 && ids[i - Width] != v);
            }
        }
        pixStart = new int[257];
        for (int k = 1; k <= 256; k++) pixStart[k] = pixStart[k - 1] + counts[k];
        pixList = new int[n];
        var fill = (int[])pixStart.Clone();
        for (int i = 0; i < n; i++) pixList[fill[ids[i]]++] = i;

        pixels = new Color32[n];
        for (int i = 0; i < n; i++) pixels[i] = BaseColor(i);
        Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Texture.SetPixels32(pixels);
        Texture.Apply();
    }

    Color32 BaseColor(int i)
    {
        byte v = ids[i];
        if (v == Ocean) return SeaColor;
        if (edge[i]) return LineColor;
        if (v == NoPeople) return EmptyLandColor;
        if (v == selectId) return SelectColor;
        if (v == hoverId) return HoverColor;
        return LandColor;
    }

    // 地図の位置（左下 0,0 〜 右上 1,1）にある国。海や人の住まない土地なら null
    public Country CountryAt(Vector2 uv)
    {
        if (uv.x < 0 || uv.x >= 1 || uv.y < 0 || uv.y >= 1) return null;
        int x = Mathf.Clamp((int)(uv.x * Width), 0, Width - 1);
        int y = Mathf.Clamp((int)(uv.y * Height), 0, Height - 1);
        return byId[ids[y * Width + x]];
    }

    public Country Get(int id) => id > 0 && id < 256 ? byId[id] : null;

    // マウスが乗っている国（明るく）と、選んだ国（オレンジ）を塗り分ける。0 = なし
    public void SetHighlight(int hover, int select)
    {
        if (hover == hoverId && select == selectId) return;
        int oldHover = hoverId, oldSelect = selectId;
        hoverId = hover;
        selectId = select;
        Repaint(oldHover);
        Repaint(oldSelect);
        Repaint(hover);
        Repaint(select);
        Texture.SetPixels32(pixels);
        Texture.Apply();
    }

    void Repaint(int id)
    {
        if (id <= 0 || id >= 255) return;
        for (int k = pixStart[id]; k < pixStart[id + 1]; k++)
        {
            int i = pixList[k];
            pixels[i] = BaseColor(i);
        }
    }

    // 人数を「1億2,595万人」のような日本語にする
    public static string FormatPeople(long n)
    {
        if (n < 10000) return n.ToString("N0") + "人";
        long oku = n / 100000000;
        long man = n % 100000000 / 10000;
        if (oku == 0) return man.ToString("N0") + "万人";
        return man == 0 ? oku + "億人" : oku + "億" + man.ToString("N0") + "万人";
    }
}
