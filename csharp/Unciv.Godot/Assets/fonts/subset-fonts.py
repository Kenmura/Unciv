#!/usr/bin/env python3
"""C025: rebuilds NotoSansCJK-subset.otf, the CJK fallback font, from the system Noto Sans CJK collection.

The full collection is ~19 MB per weight; the game only needs the characters its shipped translations use, so this keeps those (plus CJK
punctuation and full-width forms). Run again after adding or updating CJK translations; the l10n-sheet scenario fails on any missing glyph.

  python3 subset-fonts.py /path/to/NotoSansCJK-Regular.ttc <repo>/android/assets/jsons/translations   (needs: pip install fonttools brotli)

Source of the collection: https://github.com/notofonts/noto-cjk (SIL OFL 1.1, see OFL.txt). The SC face (index 2) is used for every CJK language.
"""
import sys, os
from fontTools.ttLib import TTCollection
from fontTools import subset

ttc, translations = sys.argv[1], sys.argv[2]
chars = set(range(0x3000, 0x3040)) | set(range(0xFF00, 0xFFF0))
for name in ("Japanese", "Korean", "Simplified_Chinese", "Traditional_Chinese"):
    with open(os.path.join(translations, name + ".properties"), encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or " = " not in line:
                continue
            chars.update(ord(c) for c in line.split(" = ", 1)[1])
font = TTCollection(ttc).fonts[2]
options = subset.Options()
options.layout_features = ["*"]
options.name_IDs = ["*"]
options.notdef_outline = True
sub = subset.Subsetter(options)
sub.populate(unicodes=sorted(c for c in chars if c > 0x7F))
sub.subset(font)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "NotoSansCJK-subset.otf")
font.save(out)
print(out, os.path.getsize(out), "bytes,", len(chars), "code points requested")
