namespace UwView.Core;

/// <summary>ファイル一覧・検索結果から開くときの動き（<see cref="FileTabGroup{T}.Decide"/> の答え）。</summary>
public enum FileOpenAction
{
    /// <summary>メインに出ている。メインに切り替えるだけ。</summary>
    ShowMain,
    /// <summary>追加タブで開いている。そのタブに切り替えるだけ（同じファイルを2つ開かない）。</summary>
    ShowTab,
    /// <summary>追加タブで新しく開く。</summary>
    AddTab,
    /// <summary>メインで開く（uvf は中身を差し替える・uvp は束ねた本文の中で移動する）。</summary>
    OpenInMain,
    /// <summary>タブが上限。開かない（古いタブを勝手に閉じない）。</summary>
    RefuseFull,
}

/// <summary>
/// 1つの結果セット（uvf）・束ねた索引（uvp）から開いたタブの組
/// （実装指示書_uvf複数ファイルGUI表示とタブ §5.3〜§5.5）。
///
/// <b>タブは最大 8 個（メイン 1 ＋ 追加 7）</b>。数えるのはこの組のタブだけ
/// （ファイル→開く などで開いたタブは数えない。§9 Q1 の案）。
/// </summary>
/// <typeparam name="T">タブ（画面では DocumentTabViewModel）。</typeparam>
public sealed class FileTabGroup<T>(T main) where T : class
{
    /// <summary>メインを含めたタブの上限。</summary>
    public const int MaxTabs = 8;

    private readonly Dictionary<int, T> _extra = [];

    /// <summary>メイン（uvf は今見ているファイル、uvp は束ねた本文）。</summary>
    public T Main { get; set; } = main;

    /// <summary>メインに出ているファイルの番号（0 始まり。束ねた本文のように1本に決まらなければ -1）。</summary>
    public int MainFile { get; set; } = -1;

    /// <summary>メインを含めたタブの数。</summary>
    public int Count => 1 + _extra.Count;

    /// <summary>追加タブを開いているか。</summary>
    public bool HasExtraTabs => _extra.Count > 0;

    /// <summary>そのファイルが開いている追加タブ（無ければ null）。</summary>
    public T? TabOf(int file) => _extra.GetValueOrDefault(file);

    /// <summary>そのファイルがどこに出ているか（一覧の「メイン」「タブ」の印）。</summary>
    public FileOpenAction? Placement(int file)
        => file == MainFile ? FileOpenAction.ShowMain
         : _extra.ContainsKey(file) ? FileOpenAction.ShowTab
         : null;

    /// <summary>ファイルを開く頼みにどう応えるか。</summary>
    /// <param name="inTab">「タブで開く」がオンか。</param>
    public FileOpenAction Decide(int file, bool inTab)
    {
        if (Placement(file) is { } already) return already;
        if (!inTab) return FileOpenAction.OpenInMain;
        return Count >= MaxTabs ? FileOpenAction.RefuseFull : FileOpenAction.AddTab;
    }

    /// <summary>追加タブを組に入れる（上限・重複なら false）。</summary>
    public bool TryAdd(int file, T tab)
    {
        if (Count >= MaxTabs || file == MainFile || _extra.ContainsKey(file)) return false;
        _extra[file] = tab;
        return true;
    }

    /// <summary>閉じた追加タブを組から外す（組のタブでなければ false）。</summary>
    public bool Remove(T tab)
    {
        foreach (var (file, t) in _extra)
            if (ReferenceEquals(t, tab)) { _extra.Remove(file); return true; }
        return false;
    }

    /// <summary>この組のタブか（メインを含む）。</summary>
    public bool Contains(T tab) => ReferenceEquals(Main, tab) || _extra.ContainsValue(tab);
}
