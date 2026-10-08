#!/usr/bin/env python3
# ダーク用のツールバーのアイコンを作る（v1.8.2 extFS E-4）。
#
# 原本（press-kit/assets/toolbar・黒＋透明度）の黒い所だけを明るい灰色にして、
# press-kit/assets/toolbar-dark/ に同じ名前で書く。色の付いた所（ペンの赤い下線など）はそのまま残す。
# 原本を足したり描き直したりしたら、これも走らせる:
#   python3 build/make-dark-toolbar-icons.py
import glob
import os
from PIL import Image

HERE = os.path.dirname(__file__)
SRC = os.path.join(HERE, '..', 'press-kit', 'assets', 'toolbar')
OUT = os.path.join(HERE, '..', 'press-kit', 'assets', 'toolbar-dark')
LIGHT = (0xE0, 0xE0, 0xE0)

os.makedirs(OUT, exist_ok=True)
for path in sorted(glob.glob(os.path.join(SRC, '*.png'))):
    im = Image.open(path).convert('RGBA')
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            # 灰色の暗い所（黒い線）だけを明るくする。色の付いた所は残す
            if max(r, g, b) < 140 and max(r, g, b) - min(r, g, b) < 40:
                px[x, y] = (*LIGHT, a)
    im.save(os.path.join(OUT, os.path.basename(path)))
print(f'{len(glob.glob(os.path.join(OUT, "*.png")))} icons -> {os.path.relpath(OUT)}')
