namespace UwView.Core;

/// <summary>
/// シンボリックリンク越しに指定されたファイルの実体。
///
/// <see cref="FileInfo"/> はリンクそのもの（lstat）を見るため、リンクを渡すと
/// 長さ・更新時刻がリンク自身の値（パス文字列の長さ等）になる。
/// 一方 FileStream / mmap はリンク先を開くので、長さを FileInfo から取ると食い違い、
/// mmap の容量不足（"The capacity may not be smaller than the file size."）で落ちる。
/// 長さ・更新時刻は必ずここを通して実体から取る。
/// </summary>
public static class LinkedFile
{
    /// <summary>リンクなら最終的なリンク先、そうでなければ自身の FileInfo。</summary>
    public static FileInfo Info(string path)
    {
        // 相対パスのままだと相対リンク（ln -s real.log link.log）の解決先が狂うので絶対パスにする
        var info = new FileInfo(Path.GetFullPath(path));
        return info.LinkTarget is not null
               && File.ResolveLinkTarget(info.FullName, returnFinalTarget: true) is FileInfo target
            ? target
            : info;
    }
}
