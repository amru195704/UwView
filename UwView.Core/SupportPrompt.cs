namespace UwView.Core;

/// <summary>
/// 支援の案内（帯）の記録。設定ファイルに入れて手元だけで持つ（外部へは何も送らない）。
/// 日はすべて端末のその日の日付の通し番号（<see cref="DateOnly.DayNumber"/>。0＝まだ無い）。
/// </summary>
public sealed class SupportPromptState
{
    /// <summary>案内を出さない（設定の「支援の案内を表示しない」。帯の［もう表示しない］でもオンになる）。</summary>
    public bool Disabled { get; set; }

    /// <summary>帯の［支援する］を押した（以後は年に 1 回の呼びかけも含めて出さない）。</summary>
    public bool Supported { get; set; }

    /// <summary>初めて使った日（この記録を始めた日）。</summary>
    public int FirstUseDay { get; set; }

    /// <summary>最後に使った日。</summary>
    public int LastUseDay { get; set; }

    /// <summary>使った日の数（同じ日に何度起動しても 1 日）。</summary>
    public int UsedDays { get; set; }

    /// <summary>最後に帯を出した日。</summary>
    public int LastShownDay { get; set; }

    /// <summary>1 か月ほど使った人への帯（年に 1 回の呼びかけを含む）を出した回数。</summary>
    public int ShownCount { get; set; }

    /// <summary>年に 1 回の呼びかけを最後に出した年（12/15 に始まる期間の年。0＝まだ）。</summary>
    public int LastSeasonYear { get; set; }
}

/// <summary>どの帯を出すか。</summary>
public enum SupportPromptKind
{
    /// <summary>出さない。</summary>
    None,
    /// <summary>1 か月ほど使った人への最初の帯（案 A）。</summary>
    First,
    /// <summary>最初の帯から 90 日後に 1 回だけ出す帯（案 A の［後で］）。</summary>
    Reminder,
    /// <summary>年に 1 回の呼びかけ（案 C。12/15〜1/10）。</summary>
    Season,
}

/// <summary>
/// 支援の案内を出すかどうかの決まり（指示-20261112-07・UwDiff と同じ決まり）。
/// 画面にもファイルにも触らない（日付を渡して決めるだけ）。帯の表示と保存は呼び出し側。
/// </summary>
public static class SupportPrompt
{
    /// <summary>受け皿（GitHub Sponsors だけ）。</summary>
    public const string SponsorsUrl = "https://github.com/sponsors/amru195704";

    /// <summary>初めて使った日から、この日数がたってから出す。</summary>
    public const int MinDaysSinceFirstUse = 30;

    /// <summary>使った日がこの日数以上の人にだけ出す（たまに開くだけの人には出さない）。</summary>
    public const int MinUsedDays = 10;

    /// <summary>帯と帯のあいだは少なくともこの日数あける（［後で］から 90 日後に 1 回）。</summary>
    public const int MinDaysBetween = 90;

    /// <summary>1 か月ほど使った人への帯の回数の上限（最初の 1 回＋［後で］の 1 回）。</summary>
    public const int MaxFirstPrompts = 2;

    /// <summary>
    /// この版が帯を出してよいか。無料版の本体だけが起動時に true にする（既定は false。
    /// 同じ部品を使う別のアプリでは出さない）。
    /// </summary>
    public static bool Available { get; set; }

    /// <summary>
    /// 使ったことを記録する（起動ごとに呼ぶ）。同じ日の 2 回目以降は数えない。
    /// 記録が変わったら true（呼び出し側が保存する）。
    /// </summary>
    public static bool RecordUse(SupportPromptState s, DateOnly today)
    {
        int d = today.DayNumber;
        if (s.LastUseDay == d) return false;
        if (s.FirstUseDay == 0) s.FirstUseDay = d;
        s.LastUseDay = d;
        s.UsedDays++;
        return true;
    }

    /// <summary>12/15〜1/10 の期間なら、その期間の始まりの年。期間の外は 0。</summary>
    public static int SeasonYear(DateOnly day)
        => day.Month == 12 && day.Day >= 15 ? day.Year
         : day.Month == 1 && day.Day <= 10 ? day.Year - 1
         : 0;

    /// <summary>今日、どの帯を出すか（出さないなら <see cref="SupportPromptKind.None"/>）。</summary>
    public static SupportPromptKind Decide(SupportPromptState s, DateOnly today)
    {
        if (s.Disabled || s.Supported) return SupportPromptKind.None;
        int d = today.DayNumber;
        if (s.FirstUseDay == 0 || d - s.FirstUseDay < MinDaysSinceFirstUse) return SupportPromptKind.None;
        if (s.UsedDays < MinUsedDays) return SupportPromptKind.None;
        if (s.LastShownDay != 0 && d - s.LastShownDay < MinDaysBetween) return SupportPromptKind.None;

        int season = SeasonYear(today);
        if (season != 0 && s.LastSeasonYear != season) return SupportPromptKind.Season;

        return s.ShownCount switch
        {
            0 => SupportPromptKind.First,
            < MaxFirstPrompts => SupportPromptKind.Reminder,
            _ => SupportPromptKind.None,
        };
    }

    /// <summary>帯を出したことを記録する（出した時点で呼ぶ。ボタンを押さずに閉じても［後で］と同じ扱い）。</summary>
    public static void MarkShown(SupportPromptState s, SupportPromptKind kind, DateOnly today)
    {
        if (kind == SupportPromptKind.None) return;
        s.LastShownDay = today.DayNumber;
        if (kind == SupportPromptKind.Season)
        {
            s.LastSeasonYear = SeasonYear(today);
            if (s.ShownCount == 0) s.ShownCount = 1;   // 呼びかけが最初の帯を兼ねる（続けて 2 回出さない）
        }
        else
        {
            s.ShownCount++;
        }
    }

    /// <summary>
    /// 起動したときの一連の手順（指示-20261113-05）: 使ったことを記録し、今日出す帯を決め、出すなら「出した」と記録する。
    /// 出した時点で記録するので、ボタンを押さずに閉じても［後で］と同じ扱いになる。
    /// 記録が変わったら <paramref name="changed"/> が true（呼び出し側が保存する）。
    /// </summary>
    public static SupportPromptKind Begin(SupportPromptState s, DateOnly today, out bool changed)
    {
        changed = RecordUse(s, today);
        var kind = Decide(s, today);
        if (kind != SupportPromptKind.None)
        {
            MarkShown(s, kind, today);
            changed = true;
        }
        return kind;
    }

    /// <summary>［支援する］を押した。以後は出さない。</summary>
    public static void MarkSupported(SupportPromptState s) => s.Supported = true;

    /// <summary>［もう表示しない］を押した。設定の「支援の案内を表示しない」と同じ。</summary>
    public static void MarkNeverShow(SupportPromptState s) => s.Disabled = true;
}
