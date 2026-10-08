using System.IO;
using UwView.Core;
using UwView.Localization;

namespace UwView.Services;

/// <summary>
/// 開いているファイルが外で変わったときの決まり（v1.8.2 extFS E-0。uvf・uvp の画面と試験用の口で共用）。
///
/// 末尾追従がオフなら帯で知らせて［読み直す］を待つ。オンなら <c>tail -F</c> と同じく自動でついていく:
/// 切り詰め・作り直し・ローテーションは読み直し、消されたら同じ名前のファイルができるまで待つ
///（できるとセッションの変化が「置き換わった」に変わるので、そこで読み直す）。
/// </summary>
public static class FileFollow
{
    public enum Step
    {
        /// <summary>何もしない。</summary>
        None,
        /// <summary>帯で知らせて［読み直す］を待つ。</summary>
        Banner,
        /// <summary>帯で知らせて、同じ名前のファイルができるのを待つ（追従中に消された）。</summary>
        Wait,
        /// <summary>読み直して末尾へ移り、追従を続ける。</summary>
        Reload,
    }

    public static Step Decide(FileChange change, bool following) => change switch
    {
        FileChange.None or FileChange.Grew => Step.None,
        FileChange.Deleted => following ? Step.Wait : Step.Banner,
        _ => following ? Step.Reload : Step.Banner,
    };

    /// <summary>帯に出す文（読み直す前）。</summary>
    public static string Notice(FileChange change, string path, bool following)
    {
        string name = Path.GetFileName(path);
        var L = Localizer.Instance;
        return change switch
        {
            FileChange.Truncated => L.Format("ChangeTruncated", name),
            FileChange.Rewritten => L.Format("ChangeRewritten", name),
            FileChange.Replaced => L.Format("ChangeReplaced", name),
            FileChange.Deleted => L.Format(following ? "ChangeDeletedWaiting" : "ChangeDeleted", name),
            _ => "",
        };
    }

    /// <summary>追従中に自動で読み直したあと、帯に 4 秒出す文。</summary>
    public static string Reloaded(FileChange change, string path)
    {
        string name = Path.GetFileName(path);
        var L = Localizer.Instance;
        return change switch
        {
            FileChange.Truncated => L.Format("FollowReloadedTruncated", name),
            FileChange.Rewritten => L.Format("FollowReloadedRewritten", name),
            _ => L.Format("FollowReopened", name),
        };
    }

    /// <summary>［読み直す］を出すか（消されて、まだ同じ名前のファイルが無いときは読み直せない）。</summary>
    public static bool CanReload(FileChange change) => change is not FileChange.Deleted;

    /// <summary>帯を自動で消すまでの時間（追従中に読み直したとき）。</summary>
    public static readonly System.TimeSpan ReloadedNoticeTime = System.TimeSpan.FromSeconds(4);
}
