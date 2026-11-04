using System;
using System.Collections.Generic;

// 名字の広がり方（ゲームのルール）。Unity に依存しない。
//
// 1 日ごとに:
//   ・国の中で増える（結婚で名字が受けつがれていくイメージ。ロジスティック。話題性・強化・結婚ブームで速くなる）
//   ・近い国へしみ出す（国境で接する国と、海をはさんで近い国）
//   ・飛行機・船で遠くの国へ飛ぶ（人口の多い国ほど行き先に選ばれやすい）
//   ・知名度ポイントのマーク（バズ）が出る → プレイヤーがクリックで拾う
//
// 名字の「人数」と「珍しさ」の取り合い:
//   多い名字（よくある名字の表にある）… 最初からその国に大勢いるが、話題にならないので増え方が遅く、マークも出にくい
//   珍しい名字（表に無い）…………………… 最初は RareStartPeople 人だけだが、話題性が高く、増え方が速くマークもよく出る
//   （珍しい名字ほど、伝統を守る会に気づかれにくい）
//
// 伝統を守る会（じゃまをする側）:
//   ・警戒度（0〜100）… 名字が広まるほど上がる。珍しい名字ほど上がりにくい
//   ・元に戻そう運動（0〜100%）… 警戒度が 30 を超えると進み始める。100% になったら負け
//   ・名字変更禁止法 … 警戒度が 50 を超えると、名字の人がいる国にときどき出る。その国では増え方が落ち、外から入れなくなる
// 世界の 99.9% が「〇〇さん」になったら勝ち。
public class NameSpread
{
    // ---- 調整用の数 ----
    public const double GrowthPerDay = 0.05;  // 国の中で 1 日に増える割合（人がまだ少ないうち）
    public const int RareStartPeople = 10;    // 珍しい名字の最初の人数
    public const double MinBuzz = 0.6, MaxBuzz = 1.6; // 話題性（増え方などにかける倍率）の範囲
    const double NeighborChance = 0.015;      // 近い国へしみ出す、1 日・1 か国あたりの確率（その国が十分染まっているとき）
    const double FlightChance = 0.006;        // 飛行機・船で飛ぶ、1 日・1 か国あたりの確率（その国が十分染まっているとき）
    const double ArrivePeople = 3;            // しみ出したとき・飛んだときに着く人数
    const double BubbleChance = 0.12;         // ポイントのマークが出る、1 日あたりの確率（話題性 ×1 のとき）
    public const int BubblePoints = 2;        // マーク 1 個のポイント
    // プレイヤーが国を狙う手（同じボタン）: 名字の人がいない国なら「名字の種を撒く」、いる国なら「結婚ブーム」
    public const int CampaignCost = 3;        // ポイント
    public const int CampaignDays = 30;       // 結婚ブームが続く日数（種を撒いた国も、この日数はブームになる）
    const double CampaignBoost = 2.0;         // 結婚ブーム中の国の増え方の倍率
    public const double CampaignSeedPeople = 5; // 種を撒いたときに送り込む人数
    public const double WinShare = 0.999;     // 世界のこれだけが名字の人になったら勝ち
    const double AlarmBase = 0.05, AlarmPerCountry = 0.004, AlarmPerShare = 8; // 警戒度が 1 日に上がる量のもと
    public const double MovementStart = 30;   // 警戒度がこれを超えると、元に戻そう運動が進み始める
    const double MovementPerDay = 0.25;       // 警戒度 100 のとき、運動が 1 日に進む量（%）
    public const double LawStart = 50;        // 警戒度がこれを超えると、禁止法が出始める
    const double LawChance = 0.02;            // 禁止法が出る 1 日あたりの確率（警戒度 100 のとき）
    const double LawGrowth = 0.4;             // 禁止法の国の増え方の倍率
    public const int PushBackCost = 4;        // 「運動を押し戻す」のポイント
    public const double PushBackAmount = 8;   // 押し戻す量（%）

    // 漢字を使う国（「書きやすさ」が効く）。それ以外の国は「呼びやすさ」が効く
    static readonly HashSet<string> KanjiCountries = new HashSet<string> { "JPN", "CHN", "TWN", "HKG", "MAC" };

    // ---- 強化 ----
    public class Upgrade
    {
        public string group, name, help;
        public int[] costs;   // レベルごとのポイント
        public int level;
        public int MaxLevel => costs.Length;
        public bool IsMax => level >= costs.Length;
        public int NextCost => IsMax ? 0 : costs[level];
    }

    public readonly Upgrade Marriage = new Upgrade { group = "広め方", name = "国際結婚", help = "近い国へ広がりやすくなる", costs = new[] { 4, 7, 11 } };
    public readonly Upgrade Airport = new Upgrade { group = "広め方", name = "空港で宣伝", help = "飛行機・船で遠くの国へ飛びやすくなる", costs = new[] { 5, 9, 14 } };
    public readonly Upgrade Sns = new Upgrade { group = "広め方", name = "SNSでバズる", help = "どの国でも増える速さアップ", costs = new[] { 6, 10, 15 } };
    public readonly Upgrade Celebrity = new Upgrade { group = "広め方", name = "芸能人デビュー", help = "ポイントのマークが出やすくなる", costs = new[] { 5, 10 } };
    public readonly Upgrade EasyWrite = new Upgrade { group = "名字の魅力", name = "書きやすさ", help = "漢字の国（日本・中国など）で増えやすい", costs = new[] { 4, 8, 12 } };
    public readonly Upgrade EasySay = new Upgrade { group = "名字の魅力", name = "呼びやすさ", help = "漢字以外の国で増えやすい", costs = new[] { 4, 8, 12 } };
    public readonly Upgrade Lucky = new Upgrade { group = "名字の魅力", name = "縁起の良さ", help = "どの国でもじわっと増えやすい", costs = new[] { 6, 12 } };
    public readonly Upgrade Hide = new Upgrade { group = "反発をおさえる", name = "目立たない", help = "警戒度が上がりにくくなる", costs = new[] { 5, 9, 14 } };
    public readonly Upgrade History = new Upgrade { group = "反発をおさえる", name = "由緒をアピール", help = "元に戻そう運動が進みにくくなる", costs = new[] { 6, 11, 16 } };
    public readonly Upgrade Lawyer = new Upgrade { group = "反発をおさえる", name = "弁護士団", help = "禁止法が出にくく、効き目も弱くなる", costs = new[] { 5, 10 } };
    public readonly Upgrade[] Upgrades;

    // ---- 出来事（画面に線やニュースを出すため）----
    public enum ArrivalKind { Neighbor, Flight, Campaign }
    public struct Arrival
    {
        public int from, to; // from = 0 はプレイヤーが名字の種を撒いたとき
        public ArrivalKind kind;
    }
    public readonly List<Arrival> Arrivals = new List<Arrival>(); // この 1 日に新しく名字の人が着いた国
    public readonly List<int> Bubbles = new List<int>();          // この 1 日に出たポイントのマーク（国の番号）
    public readonly List<int> NewLaws = new List<int>();          // この 1 日に禁止法が出た国
    public bool MovementBegan { get; private set; }               // この 1 日に元に戻そう運動が始まった
    public enum Outcome { None, Win, Lose }

    // ---- 状態 ----
    readonly WorldMap.Country[] countries;
    readonly WorldMap.Country[] byId = new WorldMap.Country[256];
    readonly double[] people = new double[256];    // 国の番号ごとの「〇〇さん」の人数
    readonly int[] campaignUntil = new int[256];   // 結婚ブームが続く最後の日
    readonly bool[] kanji = new bool[256];
    readonly bool[] law = new bool[256];           // 名字変更禁止法が出た国
    readonly Random rng = new Random();

    public int Day { get; private set; }
    public int StartCountry { get; private set; }
    public bool Started => StartCountry != 0;
    public double Buzz { get; private set; } = 1; // 話題性
    public double StartTotal { get; private set; }
    public double Total { get; private set; }
    public long WorldPopulation { get; }
    public int Points { get; private set; }
    public int CountriesReached { get; private set; }
    public double Alarm { get; private set; }      // 警戒度（0〜100）
    public double Movement { get; private set; }   // 元に戻そう運動（0〜100%）
    public Outcome Result { get; private set; }
    public bool Over => Result != Outcome.None;
    public int LawCount { get; private set; }

    // 珍しさ（0 = 1 億人の名字、1 = 10 人の珍しい名字）。気づかれにくさとランクに使う
    public double Rarity => (Buzz - MinBuzz) / (MaxBuzz - MinBuzz);

    public NameSpread(WorldMap.Country[] countries)
    {
        this.countries = countries;
        foreach (var c in countries)
        {
            byId[c.id] = c;
            WorldPopulation += c.population;
            kanji[c.id] = KanjiCountries.Contains(c.code);
        }
        Upgrades = new[] { Marriage, Airport, Sns, Celebrity, EasyWrite, EasySay, Lucky, Hide, History, Lawyer };
    }

    // 始める前の状態に戻す
    public void Reset()
    {
        Array.Clear(people, 0, people.Length);
        Array.Clear(campaignUntil, 0, campaignUntil.Length);
        Array.Clear(law, 0, law.Length);
        foreach (var u in Upgrades) u.level = 0;
        Arrivals.Clear();
        Bubbles.Clear();
        NewLaws.Clear();
        MovementBegan = false;
        Alarm = Movement = 0;
        Result = Outcome.None;
        LawCount = 0;
        StartCountry = 0;
        Total = StartTotal = 0;
        Points = 0;
        Day = 0;
        CountriesReached = 0;
    }

    // 最初の人数を置いて始める（国の番号 → 人数）。人数が多いほど話題性は低い
    public void Start(Dictionary<int, double> start)
    {
        Reset();
        double total = 0, most = 0;
        foreach (var kv in start)
        {
            var c = Get(kv.Key);
            if (c == null) continue;
            people[c.id] = Math.Min(kv.Value, c.population);
            total += people[c.id];
            if (people[c.id] > most) { most = people[c.id]; StartCountry = c.id; }
        }
        Total = StartTotal = total;
        Buzz = BuzzFor(total);
        Day = 1;
        CountCountries();
    }

    // 話題性: 10 人なら 1.6 倍、1 億人なら 0.6 倍（人数の桁で決める）
    public static double BuzzFor(double startPeople)
    {
        double digits = Math.Log10(Math.Max(1, startPeople));      // 10 人 = 1、1 億人 = 8
        double t = Math.Max(0, Math.Min(1, (digits - 1) / 7));
        return MaxBuzz - (MaxBuzz - MinBuzz) * t;
    }

    WorldMap.Country Get(int id) => id > 0 && id < 256 ? byId[id] : null;

    // 1 日進める。Arrivals と Bubbles に、この日の出来事が入る
    public void Step()
    {
        if (!Started || Over) return;
        Day++;
        Arrivals.Clear();
        Bubbles.Clear();
        NewLaws.Clear();
        MovementBegan = false;

        // 近い国・遠くの国へ広がる（今日の人数をもとに決めてから、まとめて足す）
        double neighborChance = NeighborChance * Buzz * (1 + 0.8 * Marriage.level);
        double flightChance = FlightChance * Buzz * (1 + 1.0 * Airport.level);
        var arrivals = new List<Arrival>();
        foreach (var c in countries)
        {
            double n = people[c.id];
            if (n < 1) continue;
            // 国の中で 5% くらいまで広まると、外へ出ていく力がいっぱいになる
            double strength = Math.Min(1, Share(c) * 20);
            foreach (int nb in c.neighbors)
                if (rng.NextDouble() < neighborChance * strength)
                    arrivals.Add(new Arrival { from = c.id, to = nb, kind = ArrivalKind.Neighbor });
            if (rng.NextDouble() < flightChance * strength)
            {
                var dest = PickByPopulation();
                if (dest != null && dest.id != c.id)
                    arrivals.Add(new Arrival { from = c.id, to = dest.id, kind = ArrivalKind.Flight });
            }
        }
        foreach (var a in arrivals)
        {
            var to = Get(a.to);
            if (to == null || to.population <= 0 || law[a.to]) continue; // 禁止法の国には入れない
            if (people[a.to] < 1) Arrivals.Add(a); // 初めて着いた国だけ知らせる
            people[a.to] = Math.Min(to.population, people[a.to] + ArrivePeople);
        }

        // 国の中で増える
        double growthAll = GrowthPerDay * Buzz * (1 + 0.25 * Sns.level) * (1 + 0.15 * Lucky.level);
        double total = 0;
        foreach (var c in countries)
        {
            double n = people[c.id];
            if (n > 0 && c.population > 0)
            {
                double g = growthAll * (kanji[c.id] ? 1 + 0.4 * EasyWrite.level : 1 + 0.3 * EasySay.level);
                if (campaignUntil[c.id] >= Day) g *= CampaignBoost;
                if (law[c.id]) g *= LawGrowth + 0.2 * Lawyer.level;
                // 増え方（ロジスティック）: 少ないうちはどんどん増え、国の人口に近づくと止まる
                n += g * n * (1 - n / c.population);
                if (n > c.population) n = c.population;
                people[c.id] = n;
            }
            total += n;
        }
        Total = total;

        // ポイントのマーク: 名字の人がいる国のどれかに出る
        double bubbleChance = BubbleChance * Buzz * (1 + 0.5 * Celebrity.level);
        while (bubbleChance > 0)
        {
            if (rng.NextDouble() < Math.Min(1, bubbleChance))
            {
                var c = PickReached();
                if (c != null) Bubbles.Add(c.id);
            }
            bubbleChance -= 1;
        }

        Points += Arrivals.Count; // 新しい国に着くたびに 1 ポイント
        CountCountries();
        StepOpposition();

        if (WorldShare >= WinShare) Result = Outcome.Win;
        else if (Movement >= 100) Result = Outcome.Lose;
    }

    // 伝統を守る会が動く
    void StepOpposition()
    {
        // 警戒度: 名字の人がいる国の数と、世界の割合で上がる。珍しい名字ほど気づかれにくい
        double notice = 1.4 - 0.9 * Rarity;
        Alarm = Math.Min(100, Alarm + notice * (1 - 0.25 * Hide.level)
                              * (AlarmBase + AlarmPerCountry * CountriesReached + AlarmPerShare * WorldShare));

        // 元に戻そう運動
        if (Alarm > MovementStart)
        {
            if (Movement == 0) MovementBegan = true;
            Movement = Math.Min(100, Movement + (Alarm - MovementStart) / (100 - MovementStart) * MovementPerDay * (1 - 0.25 * History.level));
        }

        // 名字変更禁止法: 名字の人がいる国から 1 つ
        if (Alarm > LawStart && rng.NextDouble() < LawChance * (Alarm / 100) * (1 - 0.3 * Lawyer.level))
        {
            var candidates = new List<WorldMap.Country>();
            foreach (var c in countries) if (people[c.id] >= 1 && !law[c.id]) candidates.Add(c);
            if (candidates.Count > 0)
            {
                var c = candidates[rng.Next(candidates.Count)];
                law[c.id] = true;
                LawCount++;
                NewLaws.Add(c.id);
            }
        }
    }

    public bool HasLaw(int countryId) => countryId > 0 && countryId < 256 && law[countryId];

    public bool CanPushBack => !Over && Movement > 0 && Points >= PushBackCost;

    // 元に戻そう運動を押し戻す
    public bool PushBack()
    {
        if (!CanPushBack) return false;
        Points -= PushBackCost;
        Movement = Math.Max(0, Movement - PushBackAmount);
        return true;
    }

    // 勝ったときのランク。かかった日数を、珍しい名字ほど少なく数える（珍しい名字で勝つほど自慢になる）
    public string Rank()
    {
        double score = Day / (0.7 + 0.6 * Rarity);
        return score <= 600 ? "S" : score <= 850 ? "A" : score <= 1200 ? "B" : "C";
    }

    void CountCountries()
    {
        int k = 0;
        foreach (var c in countries) if (people[c.id] >= 1) k++;
        CountriesReached = k;
    }

    // 人口の多い国ほど選ばれやすい（飛行機・船の行き先）
    WorldMap.Country PickByPopulation()
    {
        double r = rng.NextDouble() * WorldPopulation;
        foreach (var c in countries)
        {
            r -= c.population;
            if (r < 0) return c;
        }
        return null;
    }

    // 名字の人がいる国から 1 つ（人の多い国ほど選ばれやすいが、少ない国にも出るよう平方根で重みを付ける）
    WorldMap.Country PickReached()
    {
        double sum = 0;
        foreach (var c in countries) if (people[c.id] >= 1) sum += Math.Sqrt(people[c.id]);
        double r = rng.NextDouble() * sum;
        foreach (var c in countries)
        {
            if (people[c.id] < 1) continue;
            r -= Math.Sqrt(people[c.id]);
            if (r < 0) return c;
        }
        return null;
    }

    // ---- プレイヤーの手 ----

    // マークを拾った
    public void CollectBubble() => Points += BubblePoints;

    public bool CanBuy(Upgrade u) => !Over && !u.IsMax && Points >= u.NextCost;

    public bool Buy(Upgrade u)
    {
        if (!CanBuy(u)) return false;
        Points -= u.NextCost;
        u.level++;
        return true;
    }

    public bool CanCampaign(int countryId)
    {
        var c = Get(countryId);
        return !Over && c != null && c.population > 0 && Points >= CampaignCost && !Campaigning(countryId)
            && people[countryId] < c.population;
    }

    public bool Campaigning(int countryId) => countryId > 0 && countryId < 256 && campaignUntil[countryId] >= Day;

    // その国で結婚ブームを起こす: しばらく増え方が CampaignBoost 倍。まだ名字の人がいない国なら、名字の種（数人）を撒く
    public bool Campaign(int countryId)
    {
        if (!CanCampaign(countryId)) return false;
        var c = Get(countryId);
        Points -= CampaignCost;
        campaignUntil[countryId] = Day + CampaignDays;
        if (people[countryId] < 1)
        {
            people[countryId] = Math.Min(c.population, CampaignSeedPeople);
            Total += people[countryId];
            Arrivals.Add(new Arrival { from = 0, to = countryId, kind = ArrivalKind.Campaign });
            CountCountries();
        }
        return true;
    }

    public double People(int countryId) => countryId > 0 && countryId < 256 ? people[countryId] : 0;

    // その国の人口のうち「〇〇さん」の割合（0〜1）
    public double Share(WorldMap.Country c) => c.population > 0 ? people[c.id] / c.population : 0;

    public double WorldShare => WorldPopulation > 0 ? Total / WorldPopulation : 0;
}
