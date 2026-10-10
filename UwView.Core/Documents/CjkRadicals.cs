namespace UwView.Core.Documents;

/// <summary>
/// PDF から取り出した文字の「康煕部首」「CJK 部首補助」を、普通の漢字に直す（v1.8.3 extFS E-5）。
///
/// 日本語の PDF には、字形は同じでも Unicode では部首の文字（⽂ U+2F42 など）として書かれているものがある
///（フォントの ToUnicode 表がそうなっている。mac の Chrome で作った PDF など）。そのままだと「文章」で探しても当たらない。
/// NFKC で正規化すると直るが、全角英数字なども半角に変わってしまうので、この 2 つの区画だけを直す。
/// 表は Python の unicodedata.normalize('NFKC') から作った（216 字。<see cref="From"/> の i 字目を <see cref="To"/> の i 字目に）。
/// </summary>
internal static class CjkRadicals
{
    private const char First = '\u2E80';
    private const char Last = '\u2FDF';

    private const string From =
        "⺟⻳⼀⼁⼂⼃⼄⼅⼆⼇⼈⼉⼊⼋⼌⼍⼎⼏⼐⼑⼒⼓⼔⼕⼖⼗⼘⼙⼚⼛⼜⼝⼞⼟⼠⼡⼢⼣⼤⼥" +
        "⼦⼧⼨⼩⼪⼫⼬⼭⼮⼯⼰⼱⼲⼳⼴⼵⼶⼷⼸⼹⼺⼻⼼⼽⼾⼿⽀⽁⽂⽃⽄⽅⽆⽇⽈⽉⽊⽋⽌⽍" +
        "⽎⽏⽐⽑⽒⽓⽔⽕⽖⽗⽘⽙⽚⽛⽜⽝⽞⽟⽠⽡⽢⽣⽤⽥⽦⽧⽨⽩⽪⽫⽬⽭⽮⽯⽰⽱⽲⽳⽴⽵" +
        "⽶⽷⽸⽹⽺⽻⽼⽽⽾⽿⾀⾁⾂⾃⾄⾅⾆⾇⾈⾉⾊⾋⾌⾍⾎⾏⾐⾑⾒⾓⾔⾕⾖⾗⾘⾙⾚⾛⾜⾝" +
        "⾞⾟⾠⾡⾢⾣⾤⾥⾦⾧⾨⾩⾪⾫⾬⾭⾮⾯⾰⾱⾲⾳⾴⾵⾶⾷⾸⾹⾺⾻⾼⾽⾾⾿⿀⿁⿂⿃⿄⿅" +
        "⿆⿇⿈⿉⿊⿋⿌⿍⿎⿏⿐⿑⿒⿓⿔⿕";

    private const string To =
        "母龟一丨丶丿乙亅二亠人儿入八冂冖冫几凵刀力勹匕匚匸十卜卩厂厶又口囗土士夂夊夕大女" +
        "子宀寸小尢尸屮山巛工己巾干幺广廴廾弋弓彐彡彳心戈戶手支攴文斗斤方无日曰月木欠止歹" +
        "殳毋比毛氏气水火爪父爻爿片牙牛犬玄玉瓜瓦甘生用田疋疒癶白皮皿目矛矢石示禸禾穴立竹" +
        "米糸缶网羊羽老而耒耳聿肉臣自至臼舌舛舟艮色艸虍虫血行衣襾見角言谷豆豕豸貝赤走足身" +
        "車辛辰辵邑酉釆里金長門阜隶隹雨靑非面革韋韭音頁風飛食首香馬骨高髟鬥鬯鬲鬼魚鳥鹵鹿" +
        "麥麻黃黍黑黹黽鼎鼓鼠鼻齊齒龍龜龠";

    /// <summary>直す文字が 1 つも無ければ、そのまま返す。</summary>
    public static string Normalize(string text)
    {
        int first = -1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] is >= First and <= Last && From.IndexOf(text[i]) >= 0) { first = i; break; }
        if (first < 0) return text;
        var chars = text.ToCharArray();
        for (int i = first; i < chars.Length; i++)
            if (chars[i] is >= First and <= Last && From.IndexOf(chars[i]) is var at and >= 0) chars[i] = To[at];
        return new string(chars);
    }
}
