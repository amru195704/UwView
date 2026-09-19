namespace UwView.Core;

/// <summary>
/// CRC-32（IEEE 802.3・反転多項式 0xEDB88320）。gzip のトレーラ照合に使う。
///
/// なぜ自前か: 公開リポジトリ側（UwView.Core）はパッケージ参照を持たない方針なので、
/// これだけのために <c>System.IO.Hashing</c> を足さない。
/// 1バイトずつ表を引く方式は約 0.5GB/s で、gz の展開（約 2GB/s）より遅く律速になっていた
/// （3GB の gz 検索で 7.8 秒中 6 秒。2026-09-19 実測）。ARM64 は CRC32 命令で、それ以外は8バイトずつ表を引く。
/// </summary>
public static class Crc32
{
    // Table[k*256 + b]: b の後ろに k バイトの 0 が続いたときの CRC（8バイトずつ処理するための表）
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[8 * 256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        for (int i = 0; i < 256; i++)
            for (int k = 1; k < 8; k++)
                table[k * 256 + i] = (table[(k - 1) * 256 + i] >> 8) ^ table[table[(k - 1) * 256 + i] & 0xFF];
        return table;
    }

    /// <summary>途中経過を持ち回して1パスで計算する。初期値は <see cref="Initial"/>。</summary>
    public const uint Initial = 0xFFFFFFFFu;

    public static uint Update(uint state, ReadOnlySpan<byte> data)
    {
        if (System.Runtime.Intrinsics.Arm.Crc32.Arm64.IsSupported)
        {
            // ARM の CRC32 命令は gzip と同じ多項式（x86 の SSE4.2 は別の多項式なので使えない）
            while (data.Length >= 8)
            {
                state = System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32(
                    state, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(data));
                data = data[8..];
            }
            foreach (byte b in data) state = System.Runtime.Intrinsics.Arm.Crc32.ComputeCrc32(state, b);
            return state;
        }
        return UpdateSliced(state, data);
    }

    /// <summary>8バイトずつ表を引く計算（CRC32 命令の無い CPU 用。どの CPU でも試せるよう公開）。</summary>
    public static uint UpdateSliced(uint state, ReadOnlySpan<byte> data)
    {
        var t = Table;
        while (data.Length >= 8)
        {
            uint lo = state ^ System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint hi = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            state = t[7 * 256 + (lo & 0xFF)] ^ t[6 * 256 + ((lo >> 8) & 0xFF)]
                  ^ t[5 * 256 + ((lo >> 16) & 0xFF)] ^ t[4 * 256 + (lo >> 24)]
                  ^ t[3 * 256 + (hi & 0xFF)] ^ t[2 * 256 + ((hi >> 8) & 0xFF)]
                  ^ t[1 * 256 + ((hi >> 16) & 0xFF)] ^ t[hi >> 24];
            data = data[8..];
        }
        foreach (byte b in data) state = t[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }

    /// <summary>1バイトずつ表を引く素朴な計算（速い経路と突き合わせる基準）。</summary>
    public static uint UpdateBytewise(uint state, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) state = Table[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }

    /// <summary>持ち回した途中経過を最終値にする。</summary>
    public static uint Finish(uint state) => state ^ 0xFFFFFFFFu;

    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));
}
