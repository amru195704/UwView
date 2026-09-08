using UwView.ViewModels;

namespace UwView.Services;

/// <summary>
/// 抽出保存の指定を覚えておく係（指示書 F4-3「直近のオプションを記憶して再利用」）。
///
/// 保存のたびに設定へ書き、「抽出保存」でそれを入れ直す。同じ抽出を毎日繰り返す
/// 使い方（決まった列をヘッダー付きで抜く等）でチェックを付け直さずに済ませるため。
/// </summary>
public static class ExtractSaveOptions
{
    /// <summary>いま画面で選ばれている指定を覚える。</summary>
    public static void Remember(FilterResultsViewModel vm)
    {
        var s = AppSettingsRef.Current;
        s.ExtractIncludeLineNumbers = vm.IncludeLineNumbersOnSave;
        s.ExtractIncludeHeader = vm.IncludeHeaderOnSave;
        s.ExtractIncludeContext = vm.IncludeContextOnSave;
        s.Save();
    }

    /// <summary>覚えている指定を画面へ入れ直す。</summary>
    public static void ApplyTo(FilterResultsViewModel vm)
    {
        var s = AppSettingsRef.Current;
        vm.IncludeLineNumbersOnSave = s.ExtractIncludeLineNumbers;
        vm.IncludeHeaderOnSave = s.ExtractIncludeHeader;
        vm.IncludeContextOnSave = s.ExtractIncludeContext;
    }
}
