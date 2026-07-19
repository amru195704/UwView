using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UwView.Core;
using UwView.Localization;

namespace UwView.ViewModels;

/// <summary>色分けハイライタ管理ダイアログの1規則行（HlRule への双方向バインド用）。</summary>
public sealed partial class HlRuleRow : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty] private string _pattern;
    [ObservableProperty] private bool _isRegex;
    [ObservableProperty] private bool _ignoreCase;
    [ObservableProperty] private string _foreground;
    [ObservableProperty] private string _background;
    [ObservableProperty] private bool _wholeLine;
    [ObservableProperty] private bool _enabled;

    public HlRuleRow(HlRule rule, Action onChanged)
    {
        _onChanged = onChanged;
        _pattern = rule.Pattern;
        _isRegex = rule.IsRegex;
        _ignoreCase = rule.IgnoreCase;
        _foreground = rule.Foreground ?? "";
        _background = rule.Background ?? "";
        _wholeLine = rule.WholeLine;
        _enabled = rule.Enabled;
    }

    public HlRule ToRule() => new(Pattern, IsRegex, IgnoreCase,
        string.IsNullOrWhiteSpace(Foreground) ? null : Foreground.Trim(),
        string.IsNullOrWhiteSpace(Background) ? null : Background.Trim(),
        WholeLine, Enabled);

    partial void OnPatternChanged(string value) => _onChanged();
    partial void OnIsRegexChanged(bool value) => _onChanged();
    partial void OnIgnoreCaseChanged(bool value) => _onChanged();
    partial void OnForegroundChanged(string value) => _onChanged();
    partial void OnBackgroundChanged(string value) => _onChanged();
    partial void OnWholeLineChanged(bool value) => _onChanged();
    partial void OnEnabledChanged(bool value) => _onChanged();
}

/// <summary>
/// 色分けハイライタ管理（実装指示書_Ver1.1_色分けハイライタ §6）。
/// アクティブセット1つの規則を編集し、変更のたびに <see cref="Changed"/> を発火。
/// MainView はそれを受けて TextView.Highlighter を差し替え・再描画する。
/// </summary>
public sealed partial class HighlighterViewModel : ObservableObject
{
    private readonly HighlighterConfig _config;
    private HlSet _set;
    private bool _suppress;

    public ObservableCollection<HlRuleRow> Rules { get; } = new();

    /// <summary>32色パレット（色ピッカーの候補）。</summary>
    public IReadOnlyList<string> Palette => HighlightDefaults.Palette;

    /// <summary>アクティブセットの名前（内部保持。表示は選択エリア＝SelectedPreset）。</summary>
    [ObservableProperty] private string _setName = "";

    /// <summary>選択中のドロップダウン項目（保存済みセット or 同梱プリセット or「なし」）。</summary>
    [ObservableProperty] private HlSet? _selectedPreset;

    /// <summary>ドロップダウン項目（保存済みセット → なし → 同梱プリセット）。</summary>
    public ObservableCollection<HlSet> Presets { get; } = new();

    /// <summary>規則が変わってコンパイル済みハイライタを作り直すべきとき。</summary>
    public event EventHandler? Changed;
    /// <summary>アクティブセットが切り替わったとき（永続化のトリガ）。</summary>
    public event EventHandler? ActiveSetChanged;

    public HighlighterViewModel(HighlighterConfig config)
    {
        _config = config;
        // 編集は「作業コピー」で行い、保存済みセット（config.Sets）はスナップショットとして不変に保つ。
        // これで「保存」した内容を後から確実に読み戻せる（作業中の編集で壊れない）。
        _set = WorkingCopy(ResolveActiveSet(config));
        _suppress = true;
        _setName = string.IsNullOrWhiteSpace(_set.Name) ? DefaultSetName : _set.Name;
        foreach (var r in _set.Rules) Rules.Add(new HlRuleRow(r, OnRuleChanged));
        RebuildPresetList();
        _suppress = false;
        Rules.CollectionChanged += (_, _) => OnRuleChanged();
    }

    /// <summary>保存済みセットから独立した作業コピーを作る（Id は対応付けのため引き継ぐ）。</summary>
    private static HlSet WorkingCopy(HlSet src) =>
        new(src.Id, src.Name, src.Rules.ConvertAll(r => r.Clone()));

    /// <summary>互換用: 単一セットから生成（プレビュー・テスト）。</summary>
    public HighlighterViewModel(HlSet activeSet)
        : this(new HighlighterConfig { Sets = { activeSet }, ActiveSetId = activeSet.Id }) { }

    private static HlSet ResolveActiveSet(HighlighterConfig config)
    {
        var set = config.Sets.Find(s => s.Id == config.ActiveSetId) ?? config.Sets.FirstOrDefault();
        if (set is null)
        {
            set = new HlSet(Guid.NewGuid().ToString("N"), DefaultSetName, new List<HlRule>());
            config.Sets.Add(set);
            config.ActiveSetId = set.Id;
        }
        return set;
    }

    private static string DefaultSetName => Localizer.Instance["HlDefaultSetName"];
    private static string NoneLabel() => Localizer.Instance["HlPresetNone"];

    /// <summary>ドロップダウンを「保存済みセット → なし → 同梱プリセット」で作り直し、現行セットを選択表示。</summary>
    private void RebuildPresetList()
    {
        bool prev = _suppress; _suppress = true;
        Presets.Clear();
        foreach (var s in _config.Sets) Presets.Add(s);           // 保存済みセット（選ぶと読込）
        Presets.Add(new HlSet("none", NoneLabel(), new List<HlRule>())); // クリア
        foreach (var p in HighlightPresets.All()) Presets.Add(p); // 同梱プリセット（選ぶと追加）
        SelectedPreset = _config.Sets.Find(s => s.Id == _set.Id); // 現在のセットを選択表示
        _suppress = prev;
    }

    partial void OnSetNameChanged(string value)
    {
        _set.Name = value;
        if (!_suppress) Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedPresetChanged(HlSet? value)
    {
        if (_suppress || value is null) return;
        ApplyPreset(value);
    }

    private void OnRuleChanged()
    {
        if (_suppress) return;
        Sync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>編集内容を HlSet へ反映（保存・コンパイルの元）。</summary>
    private void Sync()
    {
        _set.Rules.Clear();
        foreach (var row in Rules) _set.Rules.Add(row.ToRule());
    }

    /// <summary>自動色分けのループ数（見分けやすい先頭色を N=10 で循環・§2 の推奨8〜12色）。</summary>
    private const int ColorCycle = 10;

    public void AddRule(string? color = null)
    {
        // 追加のたびに背景色を自動で変える（現在の規則数を基準に先頭パレットを循環）。文字色は既定で黒。
        color ??= HighlightDefaults.Palette[Rules.Count % Math.Min(ColorCycle, HighlightDefaults.Palette.Length)];
        Rules.Add(new HlRuleRow(new HlRule("", foreground: "#000000", background: color), OnRuleChanged));
    }

    /// <summary>既存の規則（インポート等）を末尾へ追加。</summary>
    public void AppendRule(HlRule rule) => Rules.Add(new HlRuleRow(rule, OnRuleChanged));

    /// <summary>
    /// ドロップダウン選択の適用。
    /// - 「なし」= 全クリア／- 保存済みセット = 読込（規則を置換・アクティブ切替）／- 同梱プリセット = 規則を先頭へ追加。
    /// </summary>
    public void ApplyPreset(HlSet item)
    {
        if (item.Id == "none")
        {
            _suppress = true; Rules.Clear(); _suppress = false;
            OnRuleChanged();
            return;
        }

        // 保存済みセット（config 内の実体）を選んだら丸ごと読込＆アクティブ切替
        if (_config.Sets.Contains(item))
        {
            LoadSet(item);
            return;
        }

        // 同梱プリセット: 規則を現行セットの先頭へ積む
        _suppress = true;
        int i = 0;
        foreach (var r in item.Rules)
            Rules.Insert(i++, new HlRuleRow(r.Clone(), OnRuleChanged));
        _suppress = false;
        OnRuleChanged();
    }

    /// <summary>保存済みセットのスナップショットを作業コピーとして読み込み、アクティブに切り替える。</summary>
    public void LoadSet(HlSet set)
    {
        _set = WorkingCopy(set);           // スナップショットのコピーを編集対象にする
        _config.ActiveSetId = set.Id;
        _suppress = true;
        Rules.Clear();
        foreach (var r in _set.Rules) Rules.Add(new HlRuleRow(r, OnRuleChanged));
        SetName = string.IsNullOrWhiteSpace(set.Name) ? DefaultSetName : set.Name;
        SelectedPreset = set;
        _suppress = false;
        OnRuleChanged();
        ActiveSetChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>現在選択中の保存済みセットを再読込（＝保存済み内容に戻す＝「再現」）。</summary>
    public void ReloadSelected()
    {
        if (SelectedPreset is { } sp && _config.Sets.Contains(sp)) LoadSet(sp);
    }

    /// <summary>保存候補の名前一覧（既存セット名＋「デフォルト」）。名前プロンプトのドロップダウン用。</summary>
    public IReadOnlyList<string> SaveNameCandidates()
    {
        var names = _config.Sets.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (!names.Contains(DefaultSetName)) names.Insert(0, DefaultSetName);
        return names;
    }

    /// <summary>
    /// 現在の編集内容を指定名の保存済みセット（スナップショット）として保存（同名は上書き）。
    /// 保存後はそのセットをアクティブにし、作業コピーを保存内容へ揃える。
    /// </summary>
    public void SaveAs(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Sync(); // 編集内容を作業コピー _set へ確定
        var rules = _set.Rules.ConvertAll(r => r.Clone());

        var target = _config.Sets.Find(s => s.Name == name);
        if (target is null)
        {
            target = new HlSet(Guid.NewGuid().ToString("N"), name, rules);
            _config.Sets.Add(target);
        }
        else
        {
            target.Rules = rules; // スナップショットを更新
        }
        _config.ActiveSetId = target.Id;
        _set = WorkingCopy(target); // 作業コピーを保存済みへ揃える（Id も一致）
        _suppress = true;
        SetName = name;
        RebuildPresetList();
        _suppress = false;
        Changed?.Invoke(this, EventArgs.Empty);
        ActiveSetChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRule(HlRuleRow row) => Rules.Remove(row);

    public void MoveUp(HlRuleRow row)
    {
        int i = Rules.IndexOf(row);
        if (i > 0) Rules.Move(i, i - 1);
    }

    public void MoveDown(HlRuleRow row)
    {
        int i = Rules.IndexOf(row);
        if (i >= 0 && i < Rules.Count - 1) Rules.Move(i, i + 1);
    }

    /// <summary>現在の規則からコンパイル済みハイライタを作る。</summary>
    public CompiledHighlighter Compile()
    {
        Sync();
        return CompiledHighlighter.Build(_set);
    }

    public HlSet Set => _set;
}
