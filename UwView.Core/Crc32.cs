namespace UwView.Core;

/// <summary>
/// CRC-32（IEEE 802.3・反転多項式 0xEDB88320）。gzip のトレーラ照合に使う。
///
/// なぜ自前か: 公開リポジトリ側（UwView.Core）はパッケージ参照を持たない方針なので、
/// これだけのために <c>System.IO.Hashing</c> を足さない。テーブル方式で十分速い
/// （照合するのは展開しながらの1パスで、律速は gzip 展開側）。
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>途中経過を持ち回して1パスで計算する。初期値は <see cref="Initial"/>。</summary>
    public const uint Initial = 0xFFFFFFFFu;

    public static uint Update(uint state, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) state = Table[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }

    /// <summary>持ち回した途中経過を最終値にする。</summary>
    public static uint Finish(uint state) => state ^ 0xFFFFFFFFu;

    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));
}
