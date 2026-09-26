namespace UwView.Core.Cli;

/// <summary>
/// <c>--help files</c>・<c>--help regex</c> の詳しい説明（uvf・uvp 共通。指示書 WideField v1.7 §9.3・§10.3）。
/// <c>--help</c> 本体が長くなりすぎるので、ファイルの指定と正規表現はここに分ける。例のコマンド名だけ差し替える。
/// </summary>
public static class CliHelpTopics
{
    /// <summary>詳しい説明の名前（<c>--help 名前</c>）。無ければ null。</summary>
    public static string? Find(string topic, bool ja, string tool) => topic switch
    {
        "files" => Files(ja, tool),
        "regex" => Regex(ja, tool),
        _ => null,
    };

    public static string Files(bool ja, string tool) => (ja
        ? """
          ファイルの指定（第1引数）
            必ず引用符で囲みます。シェルに展開させず、uvf が自分で広げます。
              uvf '*.log' ERROR           ○
              uvf *.log ERROR             ✕（シェルが広げてしまう。エラーになります）

            区切り
              空白かカンマで複数書けます。カンマがあるときはカンマだけで区切ります。
              名前に空白を含むパス（Program Files など）はカンマで区切ってください。
                uvf 'a.log b.log' 語
                uvf 'C:/Program Files/app/*.log,D:/logs/*.log' 語

            使える記号
              *     任意の文字の並び（フォルダーの区切り / はまたがない）
              ?     任意の1文字
              **    フォルダーを何段でも（0段も含む）
              [ ] { } は使えません（文字どおりの名前として扱います）

            例
              '*.log'              今のフォルダーの .log
              'logs/*.log'         logs の直下の .log
              '*/*.log'            1段下のフォルダーの .log（今のフォルダーの .log は含まない）
              '**/*.log'           今のフォルダー以下すべての .log（サブフォルダーも含む）
              'logs/**/*.log'      logs 以下すべての .log
              'logs/**'            logs 以下のすべてのファイル
              '**/2026-09/*.log'   どこかにある 2026-09 フォルダーの直下の .log
              'app.log,app.log.*.gz'  平文と gz を混ぜて

            決まりごと
              ・並びは書いた順。1つの指定の中は名前順です。同じファイルは1回だけ探します
              ・隠しファイルと隠しフォルダー（. で始まる名前など）は対象外です
              ・大文字と小文字：Linux は区別します。Mac と Windows は区別しません
              ・.uwvz はワイルドカードでは拾いません（名前を書けば使えます）
              ・1件も当たらない指定は、その指定を名前で知らせます
              ・1,000本を超えると知らせます（止めません。目安は UVF_MANY_FILES で変えられます）
              ・フォルダー名だけ（'logs'）は使えません。'logs/**' と書いてください
              ・シンボリックリンクのフォルダーはたどりません
              ・パスの区切りは / と \ のどちらでも書けます（Windows）

            除外（.ignore・.gitignore）
              ワイルドカードで広げたファイルのうち、.ignore と .gitignore（git のリポジトリの中だけ）に
              当たるものは探しません。ripgrep と同じ規則です。名前を書いたファイルは除外しません。
              --ignore-file <ファイル>   除外の規則を足す（何回でも）
              --no-ignore                除外をしない
              --files                    何が対象になるか確かめる（除外した本数も出します）
          """
        : """
          Specifying files (the first argument)
            Always quote it. Do not let the shell expand it; uvf expands it itself.
              uvf '*.log' ERROR           OK
              uvf *.log ERROR             NG (the shell expands it; this is an error)

            Separators
              Write several items separated by spaces or commas. If there is a comma, only commas separate.
              Use commas for paths that contain spaces (such as Program Files).
                uvf 'a.log b.log' word
                uvf 'C:/Program Files/app/*.log,D:/logs/*.log' word

            Wildcards
              *     any run of characters (does not cross the folder separator /)
              ?     any single character
              **    any number of folders (including none)
              [ ] { } are not wildcards (they are taken as part of the name)

            Examples
              '*.log'              .log files in this folder
              'logs/*.log'         .log files directly in logs
              '*/*.log'            .log files one folder down (not the ones in this folder)
              '**/*.log'           every .log file here and below (subfolders included)
              'logs/**/*.log'      every .log file under logs
              'logs/**'            every file under logs
              '**/2026-09/*.log'   .log files directly in any folder named 2026-09
              'app.log,app.log.*.gz'  plain and gz files mixed

            Rules
              - Items are searched in the order written; within one item, in name order. Each file only once
              - Hidden files and folders (such as names starting with .) are skipped
              - Case: Linux is case-sensitive; Mac and Windows are not
              - Wildcards never pick up .uwvz files (write the name to use one)
              - An item that matches nothing is reported by name
              - More than 1,000 files is reported (not stopped; change the threshold with UVF_MANY_FILES)
              - A folder name alone ('logs') is not accepted; write 'logs/**'
              - Symbolic links to folders are not followed
              - Both / and \ can be used as the path separator (Windows)

            Exclusion (.ignore / .gitignore)
              Files found by wildcards are skipped when .ignore or .gitignore (inside a git repository only)
              excludes them. The rules are the same as ripgrep's. Files you name are never excluded.
              --ignore-file <file>   add exclusion rules (repeatable)
              --no-ignore            do not exclude anything
              --files                check what is covered (the number of excluded files is shown too)
          """).Replace("uvf ", tool + " ");

    public static string Regex(bool ja, string tool) => (ja
        ? """
          正規表現（-E を付けたとき）
            UwView は .NET の正規表現を使います。ripgrep（Rust）とはほぼ同じですが、違いがあります。
            -E を付けなければ、検索語は文字列そのままで探します（ripgrep の -F と同じ）。

            よく使う書き方
              .  任意の1文字     *  0回以上     +  1回以上     ?  0か1回     {2,5}  2〜5回
              [abc] [^abc] [0-9]   ( ) グループ   a|b どちらか
              ^ 行頭   $ 行末      \d 数字   \w 英数字・日本語など   \s 空白   \b 語の境目
              (?i) 大文字小文字を区別しない（-i と同じ）

            ripgrep に無いもの（使えます）
              (?=…) (?!…) 先読み   (?<=…) (?<!…) 後読み   \1 後方参照

            使えないもの（書き換えてください）
              [[:alpha:]] など  →  \p{L}（文字）  \d（数字）  \s（空白）
              \p{Han}          →  \p{IsCJKUnifiedIdeographs}（漢字）
              \p{Hiragana}     →  \p{IsHiragana}      \p{Katakana} → \p{IsKatakana}
              （ripgrep の名前付きグループ (?P<名前>…) はそのまま使えます）

            決まりごと
              ・1行ずつ照合します（複数行にまたがる検索はできません）
              ・1回の照合が 5 秒を超えたら止めます（式を見直してください）
              ・ファイルの文字コードに関係なく、文字として照合します
          """
        : """
          Regular expressions (with -E)
            UwView uses .NET regular expressions. They are almost the same as ripgrep (Rust), with some differences.
            Without -E, the search term is matched as a plain string (like ripgrep -F).

            Common syntax
              .  any character   *  0 or more   +  1 or more   ?  0 or 1   {2,5}  2 to 5 times
              [abc] [^abc] [0-9]   ( ) group   a|b either
              ^ start of line   $ end of line   \d digit   \w word character (incl. Japanese)   \s space   \b word boundary
              (?i) ignore case (same as -i)

            Not in ripgrep (available here)
              (?=...) (?!...) lookahead   (?<=...) (?<!...) lookbehind   \1 backreference

            Not available (rewrite them)
              [[:alpha:]] and others  ->  \p{L} (letter)  \d (digit)  \s (space)
              \p{Han}                 ->  \p{IsCJKUnifiedIdeographs} (kanji)
              \p{Hiragana}            ->  \p{IsHiragana}      \p{Katakana} -> \p{IsKatakana}
              (ripgrep's named group (?P<name>...) works as it is)

            Rules
              - Each line is matched on its own (no multi-line matches)
              - A single match that takes more than 5 seconds is stopped (review the expression)
              - Text is matched as characters, whatever the file's encoding
          """);
}
