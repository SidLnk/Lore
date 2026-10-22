"""Final Lore logo masters (Album page) in Archive Green. Run: python design/logo/build_final.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_concepts as bc  # noqa: E402  (also regenerates the concept files)

GREEN = "#2B4C3F"
OUT = os.path.join(os.path.dirname(__file__), "final")
os.makedirs(OUT, exist_ok=True)


def write(name, viewbox, body, title):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as fh:
        fh.write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewbox}" role="img" aria-labelledby="title">\n'
                 f'  <title id="title">{title}</title>\n{body}</svg>\n')


SYMBOL = f'  <path fill="{GREEN}" fill-rule="evenodd" d="{bc.TILE} {bc.A4_HOLES}"/>\n'
# 16-24 px cut: one larger print with clipped corners; the separate mounts would merge at that size.
SMALL = (f'  <path fill="{GREEN}" fill-rule="evenodd" d="{bc.TILE} '
         'M76 64 H152 L192 104 V180 A12 12 0 0 1 180 192 H104 L64 152 V76 A12 12 0 0 1 76 64 Z"/>\n')
# Positive artwork (print + mounts) for the app icon foreground on a green tile.
ART = "".join(f'  <path fill="{GREEN}" d="{d}"/>\n' for d in bc.A4_HOLES.split(" Z ") if d)
ART = ART.replace('"/>', ' Z"/>').replace(" Z Z", " Z")
words, w = bc.wordmark(0)

write("lore-symbol.svg", "0 0 256 256", SYMBOL, "Lore")
write("lore-symbol-small.svg", "0 0 256 256", SMALL, "Lore")
write("lore-art.svg", "56 56 144 144", ART, "Lore")
write("lore-wordmark.svg", f"-24 0 {w + 48} 256", bc.paths(words, GREEN), "Lore")
wx = 224 + bc.GAP
write("lore-horizontal.svg", f"8 0 {wx + w + 24 - 8} 256",
      f'  <g id="symbol">\n{SYMBOL}  </g>\n  <g id="wordmark" transform="translate({wx} 0)">\n{bc.paths(words, GREEN)}  </g>\n', "Lore")
W = w + 48
write("lore-stacked.svg", f"0 0 {W} 464",
      f'  <g id="symbol" transform="translate({(W - 256) / 2} -8)">\n{SYMBOL}  </g>\n'
      f'  <g id="wordmark" transform="translate(24 240)">\n{bc.paths(words, GREEN)}  </g>\n', "Lore")
print("wrote", sorted(os.listdir(OUT)))
