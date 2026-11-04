using System;

// 名字の広がり方（ゲームのルール）。Unity に依存しない。
// いまは「国の中で人数が増える」だけ。国から国へ広がるしくみは、あとで足す。
//
// 名字の「人数」と「珍しさ」の取り合い:
//   多い名字（よくある名字の表にある）… 最初からその国に大勢いるが、話題にならないので増え方が遅い
//   珍しい名字（表に無い）…………………… 最初は RareStartPeople 人だけだが、話題性が高く増え方が速い
//   （知名度ポイントがたまりやすい・伝統を守る会に気づかれにくい、は今後のしくみで足す）
public class NameSpread
{
    // 国の中で 1 日に増える割合（人がまだ少ないうちは 1 日に約 5% ずつ増え、国の人口に近づくほどゆっくりになる）
    public const double GrowthPerDay = 0.05;
    public const int RareStartPeople = 10; // 珍しい名字の最初の人数
    public const double MinBuzz = 0.6, MaxBuzz = 1.6; // 話題性（増え方にかける倍率）の範囲

    readonly WorldMap.Country[] countries;
    readonly double[] people = new double[256]; // 国の番号ごとの「〇〇さん」の人数

    public int Day { get; private set; }
    public int StartCountry { get; private set; }
    public bool Started => StartCountry != 0;
    public double Buzz { get; private set; } = 1; // 話題性
    public double StartTotal { get; private set; }
    public double Total { get; private set; }
    public long WorldPopulation { get; }

    public NameSpread(WorldMap.Country[] countries)
    {
        this.countries = countries;
        foreach (var c in countries) WorldPopulation += c.population;
    }

    // 最初の人数を置いて始める（国の番号 → 人数）。人数が多いほど話題性は低い
    public void Start(System.Collections.Generic.Dictionary<int, double> start)
    {
        Array.Clear(people, 0, people.Length);
        StartCountry = 0;
        double total = 0, most = 0;
        foreach (var kv in start)
        {
            var c = Find(kv.Key);
            if (c == null) continue;
            people[c.id] = Math.Min(kv.Value, c.population);
            total += people[c.id];
            if (people[c.id] > most) { most = people[c.id]; StartCountry = c.id; }
        }
        Total = StartTotal = total;
        Buzz = BuzzFor(total);
        Day = 1;
    }

    // 話題性: 10 人なら 1.6 倍、1 億人なら 0.6 倍（人数の桁で決める）
    public static double BuzzFor(double startPeople)
    {
        double digits = Math.Log10(Math.Max(1, startPeople));      // 10 人 = 1、1 億人 = 8
        double t = Math.Max(0, Math.Min(1, (digits - 1) / 7));
        return MaxBuzz - (MaxBuzz - MinBuzz) * t;
    }

    WorldMap.Country Find(int id)
    {
        foreach (var c in countries) if (c.id == id) return c;
        return null;
    }

    // 1 日進める
    public void Step()
    {
        if (!Started) return;
        Day++;
        double total = 0;
        foreach (var c in countries)
        {
            double n = people[c.id];
            if (n > 0 && c.population > 0)
            {
                // 増え方（ロジスティック）: 少ないうちはどんどん増え、国の人口に近づくと止まる
                n += GrowthPerDay * Buzz * n * (1 - n / c.population);
                if (n > c.population) n = c.population;
                people[c.id] = n;
            }
            total += n;
        }
        Total = total;
    }

    public double People(int countryId) => countryId > 0 && countryId < 256 ? people[countryId] : 0;

    // その国の人口のうち「〇〇さん」の割合（0〜1）
    public double Share(WorldMap.Country c) => c.population > 0 ? people[c.id] / c.population : 0;

    public double WorldShare => WorldPopulation > 0 ? Total / WorldPopulation : 0;
}
