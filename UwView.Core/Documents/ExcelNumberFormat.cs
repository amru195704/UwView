using System.Globalization;
using System.Text;

namespace UwView.Core.Documents;

/// <summary>
/// Excel の表示形式（<c>numFmt</c>）で数値を文字にする（v1.8.3 extFS E-5。§9-4「表示されている値」）。
///
/// Excel と 1 字も違わないことは目指さない（ロケール・条件付き書式・分数などは近いもので出す）。
/// 探すのに困らないこと——日付が <c>45678</c> ではなく <c>2025/1/21</c> で、桁区切り・小数の桁・％ が見えたとおりで出ること——を目指す。
/// 日本語の Excel の既定に合わせる（形式 14 は <c>yyyy/m/d</c>）。
/// </summary>
internal static class ExcelNumberFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>組み込みの形式（番号 → 書式）。日本語の Excel の表示に合わせたもの。</summary>
    private static readonly Dictionary<int, string> BuiltIn = new()
    {
        [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00",
        [5] = "\"¥\"#,##0;\"¥\"-#,##0", [6] = "\"¥\"#,##0;[Red]\"¥\"-#,##0",
        [7] = "\"¥\"#,##0.00;\"¥\"-#,##0.00", [8] = "\"¥\"#,##0.00;[Red]\"¥\"-#,##0.00",
        [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
        [14] = "yyyy/m/d", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy",
        [18] = "h:mm AM/PM", [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "yyyy/m/d h:mm",
        [27] = "yyyy\"年\"m\"月\"", [28] = "m\"月\"d\"日\"", [29] = "m\"月\"d\"日\"", [30] = "m/d/yy",
        [31] = "yyyy\"年\"m\"月\"d\"日\"", [32] = "h\"時\"mm\"分\"", [33] = "h\"時\"mm\"分\"ss\"秒\"",
        [34] = "yyyy\"年\"m\"月\"", [35] = "m\"月\"d\"日\"", [36] = "yyyy\"年\"m\"月\"",
        [37] = "#,##0 ;(#,##0)", [38] = "#,##0 ;[Red](#,##0)", [39] = "#,##0.00;(#,##0.00)", [40] = "#,##0.00;[Red](#,##0.00)",
        [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mm:ss.0", [48] = "##0.0E+0", [49] = "@",
        [50] = "yyyy\"年\"m\"月\"", [51] = "m\"月\"d\"日\"", [52] = "yyyy\"年\"m\"月\"", [53] = "m\"月\"d\"日\"",
        [54] = "m\"月\"d\"日\"", [55] = "yyyy\"年\"m\"月\"", [56] = "m\"月\"d\"日\"", [57] = "yyyy\"年\"m\"月\"", [58] = "m\"月\"d\"日\"",
    };

    /// <summary>番号の組み込み形式（無ければ General）。</summary>
    public static string BuiltInCode(int id) => BuiltIn.TryGetValue(id, out var code) ? code : "General";

    /// <summary>数値を形式どおりの文字にする。</summary>
    /// <param name="date1904">ブックが 1904 年基準か。</param>
    public static string Format(double value, string code, bool date1904)
    {
        if (string.IsNullOrEmpty(code) || code.Equals("General", StringComparison.OrdinalIgnoreCase)) return General(value);
        var sections = Sections(code);
        // 節：正；負；ゼロ；文字。条件付き（[<100] など）は近いもので出す
        string section = value < 0 && sections.Count >= 2 ? sections[1]
                       : value == 0 && sections.Count >= 3 ? sections[2]
                       : sections[0];
        bool negativeSection = value < 0 && sections.Count >= 2;
        string clean = Clean(section, out bool elapsedHours);
        if (clean.Equals("General", StringComparison.OrdinalIgnoreCase)) return General(value);
        if (clean.Trim() == "@") return General(value);
        if (IsDate(clean))
        {
            try { return FormatDate(value, clean, date1904, elapsedHours); }
            catch (ArgumentOutOfRangeException) { return General(value); }
        }
        if (clean.Contains('/') && clean.Contains('?')) return General(value);   // 分数は近い値（小数）で
        try
        {
            double shown = negativeSection ? Math.Abs(value) : value;
            return shown.ToString(ToNetFormat(clean), Inv);
        }
        catch (FormatException) { return General(value); }
    }

    /// <summary>Excel の「標準」（有効数字 11 桁まで。整数はそのまま）。</summary>
    public static string General(double value)
    {
        if (value == 0) return "0";
        if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(Inv);
        double abs = Math.Abs(value);
        if (abs >= 1e11 || abs < 1e-9)
        {
            string e = value.ToString("0.#####E+00", Inv);
            return e;
        }
        double rounded = double.Parse(value.ToString("G11", Inv), Inv);
        return rounded.ToString("0.##########", Inv);
    }

    /// <summary>「;」で節に分ける（引用符と \ の中の ; は分けない）。</summary>
    private static List<string> Sections(string code)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '"') quoted = !quoted;
            else if (c == '\\' && i + 1 < code.Length && !quoted) { current.Append(c).Append(code[++i]); continue; }
            else if (c == ';' && !quoted) { list.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        list.Add(current.ToString());
        return list;
    }

    /// <summary>[色]・[$-411]・[条件]・_x（空白）・*x（埋め）を除く。[h] [m] [s]（経過時間）は残して印を立てる。</summary>
    private static string Clean(string section, out bool elapsed)
    {
        elapsed = false;
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];
            if (c == '"') { quoted = !quoted; sb.Append(c); continue; }
            if (quoted) { sb.Append(c); continue; }
            if (c == '\\' && i + 1 < section.Length) { sb.Append(c).Append(section[++i]); continue; }
            if (c == '_' && i + 1 < section.Length) { sb.Append(' '); i++; continue; }
            if (c == '*' && i + 1 < section.Length) { i++; continue; }
            if (c == '[')
            {
                int end = section.IndexOf(']', i);
                if (end < 0) break;
                string inner = section[(i + 1)..end];
                if (inner.Length > 0 && inner.All(ch => ch is 'h' or 'H' or 'm' or 'M' or 's' or 'S'))
                {
                    elapsed = true;
                    sb.Append(inner.ToLowerInvariant());
                }
                i = end;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 引用符の外に y・d・h・s があれば日付・時刻。m・e・g・a は、数の記号（0 # ?）が無いときだけ
    ///（<c>0.00E+00</c> の E、<c>#,##0"円"</c> などを日付と取り違えないように）。
    /// </summary>
    private static bool IsDate(string code)
    {
        bool quoted = false, placeholder = false, weak = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '\\') { i++; continue; }
            if (code.AsSpan(i).StartsWith("AM/PM", StringComparison.OrdinalIgnoreCase)) { i += 4; weak = true; continue; }
            switch (char.ToLowerInvariant(c))
            {
                case 'y' or 'd' or 'h' or 's': return true;
                case 'm' or 'e' or 'g' or 'a': weak = true; break;
                case '0' or '#' or '?': placeholder = true; break;
            }
        }
        return weak && !placeholder;
    }

    /// <summary>Excel の数値の書式を .NET の書式に（? は #、文字はそのまま。.NET も ; ・% ・E+00 ・末尾の , を同じに読む）。</summary>
    private static string ToNetFormat(string code)
    {
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in code)
        {
            if (c == '"') { quoted = !quoted; sb.Append(c); continue; }
            if (quoted) { sb.Append(c); continue; }
            sb.Append(c == '?' ? '#' : c);
        }
        return sb.ToString();
    }

    /// <summary>シリアル値を日時にする（1900 年基準は Excel の「1900/2/29」の扱いに合わせる）。</summary>
    public static DateTime ToDateTime(double serial, bool date1904)
    {
        if (date1904) return new DateTime(1904, 1, 1).AddDays(serial);
        if (serial < 60) return new DateTime(1899, 12, 31).AddDays(serial);
        return new DateTime(1899, 12, 30).AddDays(serial);
    }

    private static readonly string[] MonthsShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] MonthsLong = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] DaysShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] DaysLong = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] DaysJa = ["日", "月", "火", "水", "木", "金", "土"];

    private static string FormatDate(double serial, string code, bool date1904, bool elapsed)
    {
        // 秒の小数まで丸める（0.1 秒より細かい誤差で 59.9999 秒が 59 秒に見えないように）
        var dt = ToDateTime(Math.Round(serial * 86400 * 1000) / 86400 / 1000, date1904);
        bool ampm = code.Contains("AM/PM", StringComparison.OrdinalIgnoreCase) || code.Contains("A/P", StringComparison.OrdinalIgnoreCase);
        var tokens = Tokenize(code);
        // m は、直前の時（h）か直後の秒（s）と並ぶときは「分」
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'm' || tokens[i].Text.Length > 2) continue;
            bool afterHour = PrevDateToken(tokens, i) is 'h';
            bool beforeSecond = NextDateToken(tokens, i) is 's';
            if (afterHour || beforeSecond) tokens[i] = tokens[i] with { Kind = 'n' };
        }
        var sb = new StringBuilder();
        foreach (var (kind, text) in tokens)
        {
            int n = text.Length;
            switch (kind)
            {
                case 'L': sb.Append(text); break;
                case 'y' or 'e': sb.Append(n <= 2 && kind == 'y' ? (dt.Year % 100).ToString("00", Inv) : dt.Year.ToString(Inv)); break;
                case 'g': break;   // 和暦の元号は出さない（年は西暦で出る）
                case 'm':
                    sb.Append(n switch { 1 => dt.Month.ToString(Inv), 2 => dt.Month.ToString("00", Inv), 3 => MonthsShort[dt.Month - 1], 5 => MonthsLong[dt.Month - 1][..1], _ => MonthsLong[dt.Month - 1] });
                    break;
                case 'd':
                    sb.Append(n switch { 1 => dt.Day.ToString(Inv), 2 => dt.Day.ToString("00", Inv), 3 => DaysShort[(int)dt.DayOfWeek], _ => DaysLong[(int)dt.DayOfWeek] });
                    break;
                case 'a':   // aaa・aaaa（日本語の曜日）
                    sb.Append(n >= 4 ? DaysJa[(int)dt.DayOfWeek] + "曜日" : DaysJa[(int)dt.DayOfWeek]);
                    break;
                case 'h':
                {
                    int hour = elapsed ? (int)Math.Floor(serial * 24) : dt.Hour;
                    if (ampm && !elapsed) hour = hour % 12 == 0 ? 12 : hour % 12;
                    sb.Append(n >= 2 ? hour.ToString("00", Inv) : hour.ToString(Inv));
                    break;
                }
                case 'n': sb.Append(n >= 2 ? dt.Minute.ToString("00", Inv) : dt.Minute.ToString(Inv)); break;
                case 's': sb.Append(n >= 2 ? dt.Second.ToString("00", Inv) : dt.Second.ToString(Inv)); break;
                case 'f': sb.Append((dt.Millisecond / 1000.0).ToString("." + new string('0', n - 1), Inv).TrimStart('0')); break;
                case 'p': sb.Append(dt.Hour < 12 ? (text.Length > 3 ? "AM" : "A") : (text.Length > 3 ? "PM" : "P")); break;
            }
        }
        return sb.ToString();
    }

    private static char? PrevDateToken(List<(char Kind, string Text)> tokens, int i)
    {
        for (int j = i - 1; j >= 0; j--) if (tokens[j].Kind != 'L') return tokens[j].Kind;
        return null;
    }

    private static char? NextDateToken(List<(char Kind, string Text)> tokens, int i)
    {
        for (int j = i + 1; j < tokens.Count; j++) if (tokens[j].Kind is not 'L' and not 'f') return tokens[j].Kind;
        return null;
    }

    /// <summary>日付の書式を、文字（L）と記号（y m d h s など。同じ字の並び）に分ける。</summary>
    private static List<(char Kind, string Text)> Tokenize(string code)
    {
        var list = new List<(char, string)>();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '"')
            {
                int end = code.IndexOf('"', i + 1);
                if (end < 0) end = code.Length;
                list.Add(('L', code[(i + 1)..Math.Min(end, code.Length)]));
                i = end + 1;
                continue;
            }
            if (c == '\\' && i + 1 < code.Length) { list.Add(('L', code[i + 1].ToString())); i += 2; continue; }
            if (code.AsSpan(i).StartsWith("AM/PM", StringComparison.OrdinalIgnoreCase)) { list.Add(('p', "AM/PM")); i += 5; continue; }
            if (code.AsSpan(i).StartsWith("A/P", StringComparison.OrdinalIgnoreCase)) { list.Add(('p', "A/P")); i += 3; continue; }
            char lower = char.ToLowerInvariant(c);
            if (lower is 'y' or 'm' or 'd' or 'h' or 's' or 'e' or 'g' or 'a')
            {
                int j = i;
                while (j < code.Length && char.ToLowerInvariant(code[j]) == lower) j++;
                list.Add((lower, code[i..j]));
                i = j;
                continue;
            }
            if (c == '.' && i + 1 < code.Length && code[i + 1] == '0' && list.Count > 0 && list[^1].Item1 == 's')
            {
                int j = i + 1;
                while (j < code.Length && code[j] == '0') j++;
                list.Add(('f', code[i..j]));
                i = j;
                continue;
            }
            list.Add(('L', c.ToString()));
            i++;
        }
        return list;
    }
}
