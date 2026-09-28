#!/usr/bin/env python3
"""操作マニュアル（md）を PDF にする。図は md と同じフォルダーの images/ を参照する。
使い方: python3 build_manual_pdf.py <マニュアル.md> [...]   → 同じ場所に <マニュアル>.pdf
必要: pandoc・playwright（chromium）・Noto Sans CJK JP
2 行目のローカルパス（/Volumes/…）は PDF に出さない。"""
import subprocess, sys, pathlib
from playwright.sync_api import sync_playwright
css = pathlib.Path(__file__).with_name('manual-pdf.css').resolve()
with sync_playwright() as p:
    b = p.chromium.launch()
    for a in sys.argv[1:]:
        md = pathlib.Path(a).resolve(); d = md.parent
        lines = md.read_text(encoding='utf-8').split('\n')
        src = '\n'.join(l for i, l in enumerate(lines) if not (i == 1 and l.startswith('/Volumes')))
        tmp = d / '_pdf_tmp.md'; html = d / '_pdf_tmp.html'
        tmp.write_text(src, encoding='utf-8')
        subprocess.run(['pandoc', str(tmp), '-f', 'gfm', '-t', 'html5', '-s', '--metadata', 'title=' + md.stem,
                        '-c', str(css), '-o', str(html)], check=True)
        h = html.read_text(encoding='utf-8').replace('<header id="title-block-header">', '<header id="title-block-header" style="display:none">')
        html.write_text(h, encoding='utf-8')
        pg = b.new_page(); pg.goto('file://' + str(html)); pg.wait_for_load_state('networkidle')
        pg.pdf(path=str(md.with_suffix('.pdf')), format='A4', print_background=True, display_header_footer=True,
               header_template='<span></span>',
               footer_template='<div style="font-size:8px;width:100%;text-align:center;color:#888"><span class="pageNumber"></span> / <span class="totalPages"></span></div>',
               margin={'top': '16mm', 'bottom': '16mm', 'left': '14mm', 'right': '14mm'})
        tmp.unlink(); html.unlink(); print('wrote', md.with_suffix('.pdf'))
    b.close()
