# 操作マニュアルの図（模式図）を PNG で作る。出力: uvf-gui-screen.png・uvf-gui-filelist.png・uvp-gui-results.png（カレント）
# 版数や画面が変わったら、この HTML を直して作り直す。必要: playwright（chromium）・Noto Sans CJK JP
from playwright.sync_api import sync_playwright
CSS="""
*{box-sizing:border-box} body{margin:0;background:#fff;font-family:'Noto Sans CJK JP',sans-serif;color:#222}
.win{width:940px;border:1.5px solid #8a929c;border-radius:10px;overflow:hidden;box-shadow:0 2px 10px rgba(0,0,0,.12);margin:14px}
.title{background:#e9ecef;padding:7px 12px;font-size:13px;display:flex;align-items:center;gap:8px;border-bottom:1px solid #c9ced4}
.dot{width:11px;height:11px;border-radius:50%;display:inline-block}
.row{display:flex;align-items:center;gap:6px;padding:6px 10px;border-bottom:1px solid #dde1e5;font-size:12.5px;flex-wrap:wrap}
.lab{font-size:11px;color:#fff;background:#5b6b7c;border-radius:4px;padding:1px 6px;margin-right:4px;white-space:nowrap}
.tab{border:1px solid #b9c0c8;border-bottom:none;border-radius:6px 6px 0 0;padding:4px 10px;background:#f7f8f9;font-size:12px}
.tab.on{background:#fff;font-weight:600}.tab.gray{background:#c8c8c8}
.ic{display:inline-flex;flex-direction:column;align-items:center;margin:0 2px}
.ic b{width:24px;height:24px;border:1px solid #9aa4ae;border-radius:5px;background:#f4f6f8;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:400}
.ic i{font-style:normal;font-size:9.5px;color:#555;margin-top:1px;white-space:nowrap}
.inp{border:1px solid #9aa4ae;border-radius:4px;padding:2px 8px;background:#fff;min-width:110px;color:#888}
.chk{font-size:12px;white-space:nowrap}.btn{border:1px solid #9aa4ae;border-radius:4px;padding:2px 8px;background:#f4f6f8;font-size:12px;white-space:nowrap}
.body{display:flex;height:170px;border-bottom:1px solid #dde1e5;font-family:'Noto Sans Mono CJK JP',monospace;font-size:12px}
.ln{width:70px;background:#f3f4f6;border-right:1px solid #dde1e5;color:#888;text-align:right;padding:6px 8px;line-height:20px}
.txt{flex:1;padding:6px 10px;line-height:20px;white-space:nowrap;overflow:hidden}
.mm{width:34px;border-left:1px solid #dde1e5;background:linear-gradient(#fff,#fff);position:relative}
.mm .v{position:absolute;left:3px;right:3px;top:40px;height:34px;background:rgba(120,130,140,.28)}
.mm .h{position:absolute;left:4px;right:4px;height:2px;background:#f08a24}
.mm .bm{position:absolute;left:4px;right:4px;height:2px;background:#2a78d6}
.st{padding:6px 10px;font-size:12px;background:#f7f8f9;display:flex;gap:18px}
.hl{background:#ffe86b}
table{border-collapse:collapse;width:100%;font-size:12.5px} td,th{padding:4px 10px;text-align:left;border-bottom:1px solid #eceff2} th{background:#f3f4f6;font-weight:600} td.r{text-align:right}
.mark{font-size:11px;background:#e7eef8;border-radius:4px;padding:1px 6px}
"""
def ic(g,n): return f'<span class="ic"><b>{g}</b><i>{n}</i></span>'
main=f"""<div class="win" id="w">
<div class="title"><span class="dot" style="background:#ff5f57"></span><span class="dot" style="background:#febc2e"></span><span class="dot" style="background:#28c840"></span>&nbsp; UwView(uvf)-Finder Scope(v1.8.2.1)</div>
<div class="row"><span class="lab">メニュー</span>ファイル　ツール　ヘルプ　（設定… は Mac はアプリ名メニュー）</div>
<div class="row" style="padding-bottom:0"><span class="lab">タブ</span><span class="tab on">app.log ×</span><span class="tab">app-0902.log ×</span><span class="tab">…</span><span style="font-size:11px;color:#777;margin-left:8px">多いときは横にスクロール</span></div>
<div class="row"><span class="lab">ツールバー</span>{ic('📂','開く')}{ic('✕','閉じる')}{ic('文','文字コード')}{ic('#','行番号')}{ic('↦','ジャンプ')}<span class="inp" style="min-width:70px">50% / 行</span>{ic('🔖','ブックマーク')}{ic('◀','前')}{ic('▶','次')}{ic('⤓','末尾追従')}{ic('♡','お気に入り')}{ic('🎨','ハイライタ')}{ic('&gt;_','コマンドライン')}{ic('あ','言語')}</div>
<div class="row"><span class="lab">検索バー</span><span class="inp">検索語</span>{ic('🔍','検索')}{ic('⌫','クリア')}{ic('★','定義済み')}<span class="chk">☐ 正規表現</span><span class="chk">☐ 大小無視</span>{ic('◀','前へ')}{ic('▶','次へ')}{ic('☰','結果一覧')}{ic('≣','ファイル一覧')}<span style="font-size:12px">3/8,739 件</span></div>
<div class="row" style="background:#fff4c2"><span class="lab">帯</span>app.log が切り詰められました<span style="font-size:11px;color:#777;margin-left:8px">外でファイルが変わったときだけ出る</span><span class="btn" style="margin-left:auto">読み直す</span><span class="btn">×</span></div>
<div class="body"><div class="ln">128<br>129<br>130<br>131<br>132<br>133<br>134</div><div class="txt">2026-09-27 10:01:02 <span class="hl">ERROR</span> connection reset<br>2026-09-27 10:01:03 INFO retry 1<br>2026-09-27 10:01:04 INFO retry 2<br>2026-09-27 10:05:44 <span class="hl">ERROR</span> timeout<br>2026-09-27 10:05:45 WARN slow response<br>2026-09-27 10:06:00 INFO ok<br>……</div><div class="mm"><div class="h" style="top:22px"></div><div class="h" style="top:58px"></div><div class="bm" style="top:100px"></div><div class="h" style="top:130px"></div><div class="v"></div></div></div>
<div class="st"><span class="lab">ステータス</span><span>パス</span><span>行モード</span><span>位置</span><span>文字コード</span><span>通知</span><span style="margin-left:auto">索引中… 42%　キャンセル</span><span>直前の処理時間</span></div>
</div>
<div style="margin:0 14px 10px;font-size:11.5px;color:#555">右端の縦の帯はミニマップ（橙＝検索の当たり、青＝ブックマーク、灰色＝今見ている位置）。ボタンはアイコンで、名前はマウスを乗せると出ます（図では下に名前を書いています）。</div>"""
flist=f"""<div class="win" id="w" style="width:720px">
<div class="title">ファイル一覧<span style="margin-left:auto" class="chk">開く先: ◉ メイン　○ タブ　○ アプリ</span></div>
<table><tr><th>番号</th><th>ファイル</th><th style="text-align:right">当たり</th><th style="text-align:right">大きさ</th><th></th></tr>
<tr><td>1</td><td>logs/app-0901.log</td><td class="r">42</td><td class="r">1.2 GB</td><td><span class="mark">メイン</span></td></tr>
<tr><td>2</td><td>logs/app-0902.log</td><td class="r">0</td><td class="r">1.1 GB</td><td></td></tr>
<tr><td>3</td><td>logs/app-0903.log.gz</td><td class="r">86</td><td class="r">0.2 GB</td><td><span class="mark">タブ</span></td></tr></table>
<div class="row" style="border-top:1px solid #dde1e5;border-bottom:none">タブ 2 / 8<span class="btn" style="margin-left:auto">閉じる</span></div></div>
<div class="win" style="width:720px"><div class="row" style="padding-bottom:0;border-bottom:none"><span class="tab on gray">1:app-0901.log</span><span class="tab">3:app-0903.log.gz ×</span><span style="font-size:11px;color:#666;margin-left:10px">メインのタブは灰色で閉じられない。追加のタブは <code>番号:ファイル名</code></span></div></div>"""
pro=f"""<div class="win" id="w">
<div class="title">多段階・シーケンシャル検索</div>
<div class="row" style="padding-bottom:0"><span class="tab on">Tab1: ERROR (8,739)</span><span class="tab">Tab2: timeout (412)</span><span class="tab">Tab3: 含まない: dev2 (398)</span></div>
<div class="row"><span class="chk">◉ 多段階</span><span class="chk">○ シーケンシャル</span><span class="inp">語</span><span class="chk">☐ Regex</span><span class="chk">☐ 大小無視</span><span class="chk">☐ 単語</span><span class="chk">☐ 含まない</span>{ic('▶','実行')}{ic('≣','ファイル一覧')}</div>
<div class="row">{ic('Σ','集計')}<span class="inp">式</span>上位 <span class="inp" style="min-width:40px">20</span>{ic('▶','実行')}<span class="btn">CSV保存</span>{ic('←','戻る')}</div>
<div class="row">{ic('⇅','並べ替え')}<span class="inp" style="min-width:70px">式</span>{ic('▶','実行')}{ic('1.','順序')}<span class="inp" style="min-width:90px">a,b,c</span>{ic('▶','実行')}<span class="btn">保存</span></div>
<div class="row"><span>件数</span><span>前後 ±</span><span class="inp" style="min-width:40px">N</span><span>行</span><span class="chk">☑ 行番号を含める</span><span class="chk">☐ 1行目をヘッダーに</span><span class="chk">☐ 前後も保存</span><span class="btn">保存…</span></div>
<div class="body" style="height:110px"><div class="ln">128<br>512<br>7</div><div class="txt">2026-09-27 10:01:02 <span class="hl">ERROR</span> connection reset<br>2026-09-27 10:05:44 <span class="hl">ERROR</span> timeout<br>……</div></div>
</div>
<div style="margin:0 14px 10px;font-size:11.5px;color:#555">アイコンのボタンは、図では下に名前を書いています（実際の画面ではマウスを乗せると出ます）。</div>"""
with sync_playwright() as p:
    b=p.chromium.launch(); pg=b.new_page(device_scale_factor=2, viewport={'width':980,'height':800})
    for name,html in [('uvf-gui-screen',main),('uvf-gui-filelist',flist),('uvp-gui-results',pro)]:
        pg.set_content(f"<style>{CSS}</style><div id='all' style='display:inline-block'>{html}</div>")
        pg.locator('#all').screenshot(path=f'{name}.png')
    b.close()
