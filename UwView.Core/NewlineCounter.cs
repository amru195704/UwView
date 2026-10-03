using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace UwView.Core;

/// <summary>
/// 改行（'\n'）を数える。検索で行番号を出すために、読んだバイトを全部数える（当たりが 0 件でも）。
///
/// <c>Span.Count</c> は 3GB で 0.15 秒かかり、hot の検索（全体 0.38 秒）の 4 割を占めていた。rg は 0.28 秒
/// （実装指示書 2026-10-03「uvf の hot の読み速度」）。ここでは 16 バイトずつ改行と比べた結果（一致＝-1）を
/// バイトのまま足し込み、あふれる前（255 回ごと）にまとめて合計する。同じ 3GB で 0.028 秒（5 倍速い）。
/// </summary>
public static class NewlineCounter
{
    public static long Count(ReadOnlySpan<byte> span)
    {
        if (!Vector128.IsHardwareAccelerated || span.Length < 64) return span.Count((byte)'\n');

        ref byte start = ref MemoryMarshal.GetReference(span);
        var newline = Vector128.Create((byte)'\n');
        int n = span.Length, i = 0;
        long total = 0;
        while (n - i >= 64)
        {
            // 足し込み先は 4 つ。1 回の周回で各レーンは最大 1 増えるので、255 回までならあふれない
            int rounds = Math.Min((n - i) / 64, 255);
            var a0 = Vector128<byte>.Zero;
            var a1 = Vector128<byte>.Zero;
            var a2 = Vector128<byte>.Zero;
            var a3 = Vector128<byte>.Zero;
            for (int r = 0; r < rounds; r++, i += 64)
            {
                a0 -= Vector128.Equals(Vector128.LoadUnsafe(ref start, (nuint)i), newline);
                a1 -= Vector128.Equals(Vector128.LoadUnsafe(ref start, (nuint)(i + 16)), newline);
                a2 -= Vector128.Equals(Vector128.LoadUnsafe(ref start, (nuint)(i + 32)), newline);
                a3 -= Vector128.Equals(Vector128.LoadUnsafe(ref start, (nuint)(i + 48)), newline);
            }
            total += Sum(a0) + Sum(a1) + Sum(a2) + Sum(a3);
        }
        for (; i < n; i++)
            if (Unsafe.Add(ref start, i) == (byte)'\n') total++;
        return total;
    }

    private static int Sum(Vector128<byte> lanes)
        => Vector128.Sum(Vector128.WidenLower(lanes)) + Vector128.Sum(Vector128.WidenUpper(lanes));
}
