using System.Text;

namespace UwView.Core;

public sealed record DetectedEncoding(Encoding Encoding, string DisplayName, bool HasBom, int BomLength);

/// <summary>
/// ファイル先頭サンプルから使用エンコーディングと改行スタイルを推定する。
/// BOM → 厳格 UTF-8 検査 → Shift-JIS/EUC-JP スコアリング の順（§4.2）。
/// </summary>
public static class EncodingDetector
{
    private static int _providerRegistered;

    /// <summary>Shift-JIS(932) / EUC-JP(51932) を使うため CodePages プロバイダを登録（冪等）。</summary>
    public static void EnsureCodePagesRegistered()
    {
        if (Interlocked.Exchange(ref _providerRegistered, 1) == 0)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding ShiftJis
    {
        get { EnsureCodePagesRegistered(); return Encoding.GetEncoding(932); }
    }

    public static Encoding EucJp
    {
        get { EnsureCodePagesRegistered(); return Encoding.GetEncoding(51932); }
    }

    public static DetectedEncoding Detect(IByteSource src, int sampleBytes = 256 * 1024)
    {
        int len = (int)Math.Min(sampleBytes, src.Length);
        byte[] buf = new byte[Math.Max(1, len)];
        int read = len == 0 ? 0 : src.Read(0, buf.AsSpan(0, len));
        return DetectFromBuffer(buf.AsSpan(0, read));
    }

    /// <summary>非同期版（WASM の Blob など async I/O 実装用）。判定ロジックは同一。</summary>
    public static async ValueTask<DetectedEncoding> DetectAsync(IByteSource src, int sampleBytes = 256 * 1024, CancellationToken ct = default)
    {
        int len = (int)Math.Min(sampleBytes, src.Length);
        byte[] buf = new byte[Math.Max(1, len)];
        int read = len == 0 ? 0 : await src.ReadAsync(0, buf.AsMemory(0, len), ct);
        return DetectFromBuffer(buf.AsSpan(0, read));
    }

    private static DetectedEncoding DetectFromBuffer(ReadOnlySpan<byte> s)
    {
        EnsureCodePagesRegistered();

        // 1. BOM 判定
        if (s.Length >= 4 && s[0] == 0xFF && s[1] == 0xFE && s[2] == 0x00 && s[3] == 0x00)
            return new DetectedEncoding(new UTF32Encoding(false, false), "UTF-32LE", true, 4);
        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF)
            return new DetectedEncoding(new UTF8Encoding(false), "UTF-8 (BOM)", true, 3);
        if (s.Length >= 2 && s[0] == 0xFF && s[1] == 0xFE)
            return new DetectedEncoding(new UnicodeEncoding(false, false), "UTF-16LE", true, 2);
        if (s.Length >= 2 && s[0] == 0xFE && s[1] == 0xFF)
            return new DetectedEncoding(new UnicodeEncoding(true, false), "UTF-16BE", true, 2);

        // 2. BOM なし → UTF-8 妥当性検査（全 ASCII も UTF-8 扱い）
        if (IsValidUtf8(s, out _))
            return new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false, 0);

        // 3. Shift-JIS / EUC-JP スコアリング（不正バイトが少ない方、同点は SJIS 優先）
        var sjis = ScoreShiftJis(s);
        var euc = ScoreEucJp(s);

        bool pickEuc =
            euc.invalid < sjis.invalid ||
            (euc.invalid == sjis.invalid && euc.valid > sjis.valid);

        return pickEuc
            ? new DetectedEncoding(EucJp, "EUC-JP", false, 0)
            : new DetectedEncoding(ShiftJis, "Shift-JIS", false, 0);
    }

    /// <summary>末尾の未完マルチバイト列は「切れただけ」とみなし妥当扱いする。</summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> s, out bool hasMultibyte)
    {
        hasMultibyte = false;
        int i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80) { i++; continue; }

            int n;
            if ((b & 0xE0) == 0xC0)
            {
                if (b < 0xC2) return false; // overlong
                n = 1;
            }
            else if ((b & 0xF0) == 0xE0) n = 2;
            else if ((b & 0xF8) == 0xF0) { if (b > 0xF4) return false; n = 3; }
            else return false;

            if (i + n >= s.Length)
                return true; // 末尾で切れた未完シーケンス → 妥当扱いで打ち切り

            for (int j = 1; j <= n; j++)
                if ((s[i + j] & 0xC0) != 0x80) return false;

            hasMultibyte = true;
            i += n + 1;
        }
        return true;
    }

    private static (int valid, int invalid) ScoreShiftJis(ReadOnlySpan<byte> s)
    {
        int valid = 0, invalid = 0, i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80) { i++; continue; }             // ASCII
            if (b >= 0xA1 && b <= 0xDF) { i++; continue; } // 半角カナ（1バイト）

            if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
            {
                if (i + 1 < s.Length)
                {
                    byte n = s[i + 1];
                    if ((n >= 0x40 && n <= 0x7E) || (n >= 0x80 && n <= 0xFC)) { valid++; i += 2; continue; }
                }
                invalid++; i++;
            }
            else { invalid++; i++; } // 0x80, 0xA0, 0xFD-0xFF
        }
        return (valid, invalid);
    }

    private static (int valid, int invalid) ScoreEucJp(ReadOnlySpan<byte> s)
    {
        int valid = 0, invalid = 0, i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80) { i++; continue; } // ASCII

            if (b == 0x8E) // 半角カナ
            {
                if (i + 1 < s.Length && s[i + 1] >= 0xA1 && s[i + 1] <= 0xDF) { valid++; i += 2; continue; }
                invalid++; i++;
            }
            else if (b == 0x8F) // JIS X 0212（3バイト）
            {
                if (i + 2 < s.Length && s[i + 1] >= 0xA1 && s[i + 1] <= 0xFE && s[i + 2] >= 0xA1 && s[i + 2] <= 0xFE)
                { valid++; i += 3; continue; }
                invalid++; i++;
            }
            else if (b >= 0xA1 && b <= 0xFE)
            {
                if (i + 1 < s.Length && s[i + 1] >= 0xA1 && s[i + 1] <= 0xFE) { valid++; i += 2; continue; }
                invalid++; i++;
            }
            else { invalid++; i++; } // 0x80-0x8D, 0x90-0xA0, 0xFF
        }
        return (valid, invalid);
    }

    /// <summary>先頭サンプルから改行スタイルを推定。CRLF 優先、次いで LF、CR 単独。</summary>
    public static NewlineStyle DetectNewline(IByteSource src, int sampleBytes = 64 * 1024)
    {
        int len = (int)Math.Min(sampleBytes, src.Length);
        if (len == 0) return NewlineStyle.Lf;
        byte[] buf = new byte[len];
        int read = src.Read(0, buf.AsSpan(0, len));
        return DetectNewlineFromBuffer(buf.AsSpan(0, read));
    }

    /// <summary>非同期版（WASM 用）。</summary>
    public static async ValueTask<NewlineStyle> DetectNewlineAsync(IByteSource src, int sampleBytes = 64 * 1024, CancellationToken ct = default)
    {
        int len = (int)Math.Min(sampleBytes, src.Length);
        if (len == 0) return NewlineStyle.Lf;
        byte[] buf = new byte[len];
        int read = await src.ReadAsync(0, buf.AsMemory(0, len), ct);
        return DetectNewlineFromBuffer(buf.AsSpan(0, read));
    }

    private static NewlineStyle DetectNewlineFromBuffer(ReadOnlySpan<byte> buf)
    {
        int read = buf.Length;
        bool sawCr = false, sawLf = false, sawCrLf = false;
        for (int i = 0; i < read; i++)
        {
            if (buf[i] == (byte)'\r')
            {
                if (i + 1 < read && buf[i + 1] == (byte)'\n') { sawCrLf = true; i++; }
                else sawCr = true;
            }
            else if (buf[i] == (byte)'\n') sawLf = true;
        }

        if (sawCrLf) return NewlineStyle.CrLf;
        if (sawLf) return NewlineStyle.Lf;
        if (sawCr) return NewlineStyle.Cr;
        return NewlineStyle.Lf;
    }
}
