namespace UwView.Core;

/// <summary>
/// 色分けハイライタの同梱プリセット（実装指示書_Ver1.1_色分けハイライタ §3-⑤）。
/// 導入直後から使える業種別の規則セット。読み取り専用の組込みとして提供し、
/// 管理ダイアログから現行セットへ複製（規則を追加）して使う。
/// 色は背景着色中心（前景の検索強調を潰さない・§5）。名前・年・版番号は着色しない方針。
/// </summary>
public static class HighlightPresets
{
    // 汎用の意味色（信号色ベース・色覚配慮）
    private const string Red = "#FFD6D6";     // 致命/エラー
    private const string Orange = "#FFE7C2";  // 警告
    private const string Green = "#D6F5D6";   // 正常/情報
    private const string Blue = "#D6E4FF";    // リダイレクト/補助
    private const string Gray = "#E6E6E6";    // デバッグ
    private const string Purple = "#ECD9F5";  // 強調
    private const string Cyan = "#D2F0F0";    // 数値/ID

    public static List<HlSet> All() => new()
    {
        GeneralLog(), Syslog(), WebAccess(), JsonLog(), GeoJson(), Kml(), Nmea(),
    };

    private static HlRule R(string pattern, string bg, bool regex = false, bool ignoreCase = true, bool whole = false)
        => new(pattern, isRegex: regex, ignoreCase: ignoreCase, background: bg, wholeLine: whole);

    /// <summary>汎用ログレベル（全業界共通・最重要）。</summary>
    public static HlSet GeneralLog() => new("preset-general", "汎用ログレベル", new List<HlRule>
    {
        R(@"\b(FATAL|CRITICAL)\b", Red, regex: true, whole: true),
        R(@"\bERROR\b", Red, regex: true),
        R(@"\bWARN(ING)?\b", Orange, regex: true),
        R(@"\bINFO\b", Green, regex: true),
        R(@"\bDEBUG\b", Gray, regex: true),
        R(@"\bTRACE\b", Gray, regex: true),
        R(@"(Exception|Traceback|\bat\s+\w+\.)", Purple, regex: true),
    }, readOnly: true);

    /// <summary>syslog / Linux system。</summary>
    public static HlSet Syslog() => new("preset-syslog", "syslog / Linux", new List<HlRule>
    {
        R(@"\b(emerg|alert|crit|err)\b", Red, regex: true),
        R(@"\bwarning\b", Orange, regex: true),
        R(@"\b(notice|info)\b", Green, regex: true),
        R(@"\[\d+\]", Cyan, regex: true),                       // PID
        R(@"\b\d{1,3}(\.\d{1,3}){3}\b", Blue, regex: true),     // IPv4
    }, readOnly: true);

    /// <summary>Web/インフラ access（HTTP ステータス色分け）。</summary>
    public static HlSet WebAccess() => new("preset-web", "Web access (HTTP)", new List<HlRule>
    {
        R(@"\s5\d{2}\s", Red, regex: true),
        R(@"\s4\d{2}\s", Orange, regex: true),
        R(@"\s3\d{2}\s", Blue, regex: true),
        R(@"\s2\d{2}\s", Green, regex: true),
        R(@"\b\d{1,3}(\.\d{1,3}){3}\b", Cyan, regex: true),     // client IP
    }, readOnly: true);

    /// <summary>汎用 JSON ログ。</summary>
    public static HlSet JsonLog() => new("preset-json", "JSON ログ", new List<HlRule>
    {
        R("\"level\"\\s*:\\s*\"(error|fatal)\"", Red, regex: true),
        R("\"level\"\\s*:\\s*\"warn(ing)?\"", Orange, regex: true),
        R("\"level\"\\s*:\\s*\"info\"", Green, regex: true),
        R("\"(traceId|requestId|trace_id|request_id)\"", Cyan, regex: true),
        R(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", Blue, regex: true), // ISO 時刻
    }, readOnly: true);

    /// <summary>GeoJSON（作者ドメイン）。ジオメトリ種別・主要キーで色分け。JSON なので大小区別。</summary>
    public static HlSet GeoJson() => new("preset-geojson", "GeoJSON", new List<HlRule>
    {
        // ジオメトリ種別（"type": "..."）
        R("\"(Point|MultiPoint)\"", Green, regex: true, ignoreCase: false),
        R("\"(LineString|MultiLineString)\"", Blue, regex: true, ignoreCase: false),
        R("\"(Polygon|MultiPolygon)\"", Purple, regex: true, ignoreCase: false),
        R("\"(Feature|FeatureCollection|GeometryCollection)\"", Orange, regex: true, ignoreCase: false),
        // 主要キー
        R("\"coordinates\"", Cyan, regex: true, ignoreCase: false),
        R("\"(geometry|properties|features)\"", Blue, regex: true, ignoreCase: false),
        R("\"(bbox|crs|type)\"", Gray, regex: true, ignoreCase: false),
    }, readOnly: true);

    /// <summary>KML（作者ドメイン）。XML タグ種別で色分け。タグは大小区別。</summary>
    public static HlSet Kml() => new("preset-kml", "KML", new List<HlRule>
    {
        // ジオメトリ（開始/終了タグ）
        R(@"</?Point\b", Green, regex: true, ignoreCase: false),
        R(@"</?(LineString|LinearRing)\b", Blue, regex: true, ignoreCase: false),
        R(@"</?(Polygon|MultiGeometry)\b", Purple, regex: true, ignoreCase: false),
        // コンテナ／地物
        R(@"</?(Placemark|Folder|Document|kml|NetworkLink)\b", Orange, regex: true, ignoreCase: false),
        // 座標・主要要素
        R(@"</?coordinates\b", Cyan, regex: true, ignoreCase: false),
        R(@"</?(name|description|ExtendedData)\b", Blue, regex: true, ignoreCase: false),
        // スタイル
        R(@"</?(Style|StyleMap|IconStyle|LineStyle|PolyStyle|LabelStyle)\b", Gray, regex: true, ignoreCase: false),
    }, readOnly: true);

    /// <summary>GNSS NMEA（作者ドメイン・差別化の目玉）。センテンス種別で色分け。</summary>
    public static HlSet Nmea() => new("preset-nmea", "GNSS NMEA", new List<HlRule>
    {
        R(@"\$G[NP]GGA", Green, regex: true),   // 測位（Fix）
        R(@"\$G[NP]RMC", Blue, regex: true),    // 推奨最小
        R(@"\$G[NP]GSV", Orange, regex: true),  // 可視衛星
        R(@"\$G[NP]GSA", Purple, regex: true),  // 精度・DOP
        R(@"\$G[NP]VTG", Cyan, regex: true),    // 対地速度
    }, readOnly: true);
}
