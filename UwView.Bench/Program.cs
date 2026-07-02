using System.Diagnostics;
using UwView.Core;

// フェーズ3 実測ハーネス（§8 パフォーマンス目標・受入基準の検証用）
// 使い方: dotnet run --project UwView.Bench -c Release -- <ファイルパス>

if (args.Length < 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("使い方: UwView.Bench <テキストファイル>");
    return 1;
}
string path = args[0];

EncodingDetector.EnsureCodePagesRegistered();

long MB(long b) => b / (1024 * 1024);
long heapBefore = GC.GetTotalMemory(forceFullCollection: true);

var swOpen = Stopwatch.StartNew();
await using var src = new MmapByteSource(path);
var detected = EncodingDetector.Detect(src);
var newline = EncodingDetector.DetectNewline(src);
var doc = new LineDocument(src, detected, newline);
swOpen.Stop();

Console.WriteLine($"ファイル       : {path}");
Console.WriteLine($"サイズ         : {src.Length:N0} bytes ({MB(src.Length):N0} MB)");
Console.WriteLine($"文字コード判定 : {detected.DisplayName} / 改行 {newline}（open+判定 {swOpen.ElapsedMilliseconds} ms）");

// ページモード即表示の確認（索引なし）
var swPage = Stopwatch.StartNew();
var firstPage = doc.GetPage(0, 50);
var midPage = doc.GetPage(src.Length / 2, 50);
swPage.Stop();
Console.WriteLine($"ページモード   : 先頭50行+50%位置50行 取得 {swPage.ElapsedMilliseconds} ms（索引なし・即表示相当）");

// 索引構築
var swIndex = Stopwatch.StartNew();
await doc.BuildIndexAsync();
swIndex.Stop();
long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
var index = doc.Index!;
double mbPerSec = MB(src.Length) / Math.Max(0.001, swIndex.Elapsed.TotalSeconds);

Console.WriteLine($"索引構築       : {swIndex.Elapsed.TotalSeconds:F2} s（{mbPerSec:N0} MB/s）");
Console.WriteLine($"総行数         : {index.TotalLines:N0}");
Console.WriteLine($"チェックポイント: {index.CheckpointCount:N0} 件 ≒ {index.CheckpointCount * 8 / 1024.0 / 1024.0:F1} MB");
Console.WriteLine($"マネージヒープ増: {(heapAfter - heapBefore) / 1024.0 / 1024.0:F1} MB");

// 行アクセス（LRUに載らないランダム分布＝ワーストケース側）
long total = index.TotalLines;
var rnd = new Random(42);
const int N = 1000;
var times = new double[N];
var swLine = new Stopwatch();
for (int i = 0; i < N; i++)
{
    long idx = (long)(rnd.NextDouble() * total);
    swLine.Restart();
    _ = doc.GetLine(idx);
    swLine.Stop();
    times[i] = swLine.Elapsed.TotalMilliseconds;
}
Array.Sort(times);
Console.WriteLine($"GetLine×{N}     : 平均 {times.Average():F3} ms / p99 {times[(int)(N * 0.99) - 1]:F3} ms / 最大 {times[^1]:F3} ms");

// 先頭・末尾・末尾ジャンプ
swLine.Restart(); string first = doc.GetLine(0); swLine.Stop();
double tFirst = swLine.Elapsed.TotalMilliseconds;
swLine.Restart(); string last = doc.GetLine(total - 1); swLine.Stop();
Console.WriteLine($"先頭行         : \"{first}\"（{tFirst:F3} ms）");
Console.WriteLine($"末尾行         : \"{last}\"（{swLine.Elapsed.TotalMilliseconds:F3} ms）← 末尾ジャンプ相当");

// 文字コード切替（再索引なしの確認）
var idxRef = doc.Index;
swLine.Restart();
doc.Encoding = EncodingDetector.ShiftJis;
_ = doc.GetLine(total / 2);
doc.Encoding = detected.Encoding;
swLine.Stop();
Console.WriteLine($"文字コード切替 : {swLine.Elapsed.TotalMilliseconds:F3} ms / 索引同一={ReferenceEquals(idxRef, doc.Index)}（再構築なし）");

// 文字列検索（§11-①）: literal 高速パスと regex パス
long searchHits = 0;
var swSearch = Stopwatch.StartNew();
var outcome = await SearchService.SearchAsync(
    src, detected.BomLength, doc.Encoding,
    new SearchOptions("line 123456 "), b => searchHits += b.Count);
swSearch.Stop();
Console.WriteLine($"検索(literal)  : {swSearch.Elapsed.TotalSeconds:F2} s（{MB(src.Length) / Math.Max(0.001, swSearch.Elapsed.TotalSeconds):N0} MB/s）ヒット {outcome.TotalHits:N0} 件");

searchHits = 0;
swSearch.Restart();
outcome = await SearchService.SearchAsync(
    src, detected.BomLength, doc.Encoding,
    new SearchOptions(@"^line 12345678\d ", UseRegex: true), b => searchHits += b.Count);
swSearch.Stop();
Console.WriteLine($"検索(regex)    : {swSearch.Elapsed.TotalSeconds:F2} s（{MB(src.Length) / Math.Max(0.001, swSearch.Elapsed.TotalSeconds):N0} MB/s）ヒット {outcome.TotalHits:N0} 件");

var proc = Process.GetCurrentProcess();
Console.WriteLine($"WorkingSet     : {MB(proc.WorkingSet64):N0} MB（mmapページ含む・OSが回収可能）");
return 0;
