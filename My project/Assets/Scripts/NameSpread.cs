using System;

// 名字の広がり方（ゲームのルール）。Unity に依存しない。
// いまは「国の中で人数が増える」だけ。国から国へ広がるしくみは、あとで足す。
public class NameSpread
{
    // 国の中で 1 日に増える割合（人がまだ少ないうちは 1 日に約 5% ずつ増え、国の人口に近づくほどゆっくりになる）
    public const double GrowthPerDay = 0.05;

    readonly WorldMap.Country[] countries;
    readonly double[] people = new double[256]; // 国の番号ごとの「〇〇さん」の人数

    public int Day { get; private set; }
    public int StartCountry { get; private set; }
    public bool Started => StartCountry != 0;
    public double Total { get; private set; }
    public long WorldPopulation { get; }

    public NameSpread(WorldMap.Country[] countries)
    {
        this.countries = countries;
        foreach (var c in countries) WorldPopulation += c.population;
    }

    // 最初の 1 人が生まれる
    public void Start(int countryId)
    {
        Array.Clear(people, 0, people.Length);
        StartCountry = countryId;
        people[countryId] = 1;
        Total = 1;
        Day = 1;
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
                n += GrowthPerDay * n * (1 - n / c.population);
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
