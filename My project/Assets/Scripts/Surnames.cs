using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// よくある名字の表（Assets/Resources/Surnames.json）。入力された名字が、どの国にだいたい何人いるかを調べる。
// 表に無い名字は「珍しい名字」。
public static class Surnames
{
    [Serializable]
    class Entry
    {
        public string[] names;  // 漢字・かな・ローマ字などの書き方
        public string country;  // 国の記号（JPN など。Countries.json の code）
        public long people;     // だいたいの人数
    }

    [Serializable]
    class EntryList
    {
        public Entry[] surnames;
    }

    static Dictionary<string, List<Entry>> table;

    // 名字 → 国の記号ごとの人数。表に無ければ空。
    // 同じ国に同じ書き方の名字が 2 つあるとき（ローマ字の ono = 小野・大野 など）は多いほうを使う
    public static Dictionary<string, long> Find(string surname)
    {
        Load();
        var result = new Dictionary<string, long>();
        if (!table.TryGetValue(Normalize(surname), out var list)) return result;
        foreach (var e in list)
        {
            result.TryGetValue(e.country, out long n);
            result[e.country] = Math.Max(n, e.people);
        }
        return result;
    }

    static void Load()
    {
        if (table != null) return;
        table = new Dictionary<string, List<Entry>>();
        var asset = Resources.Load<TextAsset>("Surnames");
        if (asset == null)
        {
            Debug.LogError("Assets/Resources/Surnames.json がありません");
            return;
        }
        foreach (var e in JsonUtility.FromJson<EntryList>(asset.text).surnames)
        {
            foreach (var name in e.names)
            {
                string key = Normalize(name);
                if (!table.TryGetValue(key, out var list)) table[key] = list = new List<Entry>();
                if (!list.Contains(e)) list.Add(e);
            }
        }
    }

    // 書き方のゆれをそろえる: 全角・半角、大文字・小文字、アクセント記号（Müller → muller）、空白、カタカナとひらがな
    static string Normalize(string s)
    {
        s = (s ?? "").Trim();
        try
        {
            s = s.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                // アクセント記号は消す。ただし、かなの濁点・半濁点（が・ぱ）は残す
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark && ch != '゙' && ch != '゚') continue;
                sb.Append(ch);
            }
            s = sb.ToString().Normalize(NormalizationForm.FormC);
        }
        catch (Exception)
        {
            // 文字の変換が使えない環境では、そのまま比べる
        }
        // カタカナはひらがなにそろえる（サトウ → さとう）
        var chars = s.ToLowerInvariant().Replace(" ", "").ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] >= '\u30A1' && chars[i] <= '\u30F6') chars[i] = (char)(chars[i] - 0x60);
        return new string(chars);
    }
}
