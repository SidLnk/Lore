"""Generates the Lore logo concept SVGs from explicit geometry (256-unit grid, 28-unit stroke).

Run: python design/logo/build_concepts.py  -> writes design/logo/concepts/*.svg
"""
import math
import os

OUT = os.path.join(os.path.dirname(__file__), "concepts")
os.makedirs(OUT, exist_ok=True)
INK = "#000000"


def f(v):
    return f"{v:.2f}".rstrip("0").rstrip(".")


def svg(name, width, body, title, x0=0):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x0} 0 {width} 256" width="{width}" height="256" '
                 f'role="img" aria-labelledby="title">\n  <title id="title">{title}</title>\n{body}</svg>\n')


# ---------- Wordmark "lore": geometric lowercase, baseline y=200, x-height 80, ascender 24, stroke 28 ----------
STROKE, R_OUT, R_IN, MID = 28, 60, 32, 140


def ring(cx):
    return (f"M{f(cx - R_OUT)} {MID} A{R_OUT} {R_OUT} 0 1 0 {f(cx + R_OUT)} {MID} A{R_OUT} {R_OUT} 0 1 0 {f(cx - R_OUT)} {MID} Z "
            f"M{f(cx - R_IN)} {MID} A{R_IN} {R_IN} 0 1 0 {f(cx + R_IN)} {MID} A{R_IN} {R_IN} 0 1 0 {f(cx - R_IN)} {MID} Z")


def pt(cx, r, deg):
    a = math.radians(deg)
    return cx + r * math.cos(a), MID + r * math.sin(a)


def wordmark(x0, ribbon=False):
    """Path elements for 'lore' starting at x0, and the right edge. ribbon=True gives the l a bookmark tail."""
    parts = []
    lx = x0
    if ribbon:
        # The l runs past the baseline as a ribbon with a V notch: a bookmark in the family story.
        parts.append(f'M{f(lx + 14)} 24 A14 14 0 0 1 {f(lx + 28)} 38 V236 L{f(lx + 14)} 220 L{f(lx)} 236 V38 A14 14 0 0 1 {f(lx + 14)} 24 Z')
    else:
        parts.append(f'M{f(lx + 14)} 24 A14 14 0 0 1 {f(lx + 28)} 38 V186 A14 14 0 0 1 {f(lx)} 186 V38 A14 14 0 0 1 {f(lx + 14)} 24 Z')
    ox = lx + STROKE + 18 + R_OUT                     # o centre
    parts.append(("evenodd", ring(ox)))
    sx = ox + R_OUT + 18                              # r stem left
    parts.append(f'M{f(sx)} 80 H{f(sx + 28)} V200 H{f(sx)} Z '
                 f'M{f(sx)} {MID} A60 60 0 0 1 {f(sx + 60)} 80 V108 A32 32 0 0 0 {f(sx + 28)} {MID} Z')
    ex = sx + 60 + 24 + R_OUT                         # e centre (r's shoulder overhangs, so a wider gap)
    a1 = 45                                           # the bowl opens from the crossbar down to this angle
    o0, i1 = pt(ex, R_OUT, a1), pt(ex, R_IN, a1)
    # Bowl from the aperture clockwise round to the right-hand midline; crossbar runs to the outer edge.
    parts.append(f'M{f(o0[0])} {f(o0[1])} A{R_OUT} {R_OUT} 0 1 1 {f(ex + R_OUT)} {MID} '
                 f'H{f(ex + R_IN)} A{R_IN} {R_IN} 0 1 0 {f(i1[0])} {f(i1[1])} Z '
                 f'M{f(ex - 40)} 128 H{f(ex + R_OUT)} V150 H{f(ex - 40)} Z')
    return parts, ex + R_OUT


def paths(parts, fill=INK):
    out = ""
    for p in parts:
        if isinstance(p, tuple):
            out += f'  <path fill="{fill}" fill-rule="evenodd" d="{p[1]}"/>\n'
        else:
            out += f'  <path fill="{fill}" d="{p}"/>\n'
    return out


# ---------- A: Photo corners ----------
# A print (48..208, r12) held by two album corners on the diagonal (legs 72), each wrapping 16 units past it.
# Corner hypotenuses on y = x +/- 120; the print's cut edges on y = x +/- 103, a 12-unit perpendicular gap.
# v2 used legs of 96: the long gaps read as slashes at 32 px.
A_SYMBOL = (
    '  <path fill="#000000" d="M60 48 H151 L208 105 V196 A12 12 0 0 1 196 208 H105 L48 151 V60 A12 12 0 0 1 60 48 Z"/>\n'
    '  <path fill="#000000" d="M32 152 L104 224 H40 A8 8 0 0 1 32 216 Z"/>\n'
    '  <path fill="#000000" d="M152 32 H216 A8 8 0 0 1 224 40 V104 Z"/>\n')

# ---------- B: Keyhole L ----------
# Rounded tile (r48) with a keyhole cut whose stem turns right into an L foot (stem and foot 32, head r32).
yk = 100 + math.sqrt(32 ** 2 - 16 ** 2)
B_HOLE = f"M96 {f(yk)} A32 32 0 1 1 128 {f(yk)} V152 H156 A16 16 0 0 1 156 184 H108 A12 12 0 0 1 96 172 Z"
TILE = "M80 32 H176 A48 48 0 0 1 224 80 V176 A48 48 0 0 1 176 224 H80 A48 48 0 0 1 32 176 V80 A48 48 0 0 1 80 32 Z"
B_SYMBOL = f'  <path fill="#000000" fill-rule="evenodd" d="{TILE} {B_HOLE}"/>\n'

# ---------- C: Bookmark l (symbol = the ribbon l cut from a tile) ----------
C_SYMBOL = (f'  <path fill="#000000" fill-rule="evenodd" d="{TILE} '
            'M128 56 A20 20 0 0 1 148 76 V204 L128 184 L108 204 V76 A20 20 0 0 1 128 56 Z"/>\n')

svg("a-symbol.svg", 256, A_SYMBOL, "Lore, photo corners")
svg("b-symbol.svg", 256, B_SYMBOL, "Lore, keyhole L")
svg("c-symbol.svg", 256, C_SYMBOL, "Lore, bookmark l")

plain, w_plain = wordmark(0)
ribbon, w_ribbon = wordmark(0, ribbon=True)
GAP = 56
MARGIN = 24
for key, sym in (("a", A_SYMBOL), ("b", B_SYMBOL)):
    # Symbol ink spans 32..224; crop the viewBox so both outer margins are MARGIN.
    wx = 224 + GAP
    body = f'  <g id="symbol">\n{sym}  </g>\n  <g id="wordmark" transform="translate({wx} 0)">\n{paths(plain)}  </g>\n'
    svg(f"{key}-lockup.svg", int(wx + w_plain + MARGIN - (32 - MARGIN)), body, f"Lore lockup {key.upper()}", x0=32 - MARGIN)
# ---------- A iterations (round 2) ----------
A_VARIANTS = {
    # A1: two triangle corners, shorter legs (64) and wider gaps (14 -> cut line offset 20) so they survive 16-32 px.
    "a1": ('  <path fill="#000000" d="M64 48 H156 L208 100 V192 A16 16 0 0 1 192 208 H100 L48 156 V64 A16 16 0 0 1 64 48 Z"/>\n'
           '  <path fill="#000000" d="M32 160 L96 224 H40 A8 8 0 0 1 32 216 Z"/>\n'
           '  <path fill="#000000" d="M160 32 H216 A8 8 0 0 1 224 40 V96 Z"/>\n'),
    # A2: one L bracket (36 thick, legs 120) gripping the print's lower-left corner across a 12-unit gap.
    "a2": ('  <path fill="#000000" d="M104 40 H200 A16 16 0 0 1 216 56 V152 A16 16 0 0 1 200 168 H104 A16 16 0 0 1 88 152 V56 A16 16 0 0 1 104 40 Z"/>\n'
           '  <path fill="#000000" d="M40 96 H76 V180 H160 V216 H56 A16 16 0 0 1 40 200 Z"/>\n'),
    # A3: two L brackets (24 thick, legs 96) on the diagonal, holding a 120-unit print across 12-unit gaps.
    "a3": ('  <path fill="#000000" d="M80 68 H176 A12 12 0 0 1 188 80 V176 A12 12 0 0 1 176 188 H80 A12 12 0 0 1 68 176 V80 A12 12 0 0 1 80 68 Z"/>\n'
           '  <path fill="#000000" d="M32 128 H56 V200 H128 V224 H44 A12 12 0 0 1 32 212 Z"/>\n'
           '  <path fill="#000000" d="M224 128 H200 V56 H128 V32 H212 A12 12 0 0 1 224 44 Z"/>\n'),
}
import re


def scale_path(d, s, c=128.0):
    """Scale an absolute M/L/H/V/A/Z path about (c, c) by s."""
    out, tokens = [], re.findall(r"[MLHVAZ]|-?\d+(?:\.\d+)?", d)
    i, cmd = 0, None
    t = lambda v: f(c + (float(v) - c) * s)
    while i < len(tokens):
        if tokens[i].isalpha():
            cmd = tokens[i]; out.append(cmd); i += 1
            if cmd == "Z":
                continue
        if cmd in "ML":
            out += [t(tokens[i]), t(tokens[i + 1])]; i += 2
        elif cmd in "HV":
            out.append(t(tokens[i])); i += 1
        elif cmd == "A":
            rx, ry, rot, la, sw, x, y = tokens[i:i + 7]
            out += [f(float(rx) * s), f(float(ry) * s), rot, la, sw, t(x), t(y)]; i += 7
    return " ".join(out)


# A4: the album page. The rounded tile is the page; a print (72..184, r10) and two corner mounts (legs 56,
# wrapping 12 past the print) are cut out of it. Drawn at this size rather than scaled from A1, so the
# 12-unit gaps (cut lines offset 17 on the diagonal) stay open at 32 px.
A4_HOLES = ("M82 72 H135 L184 121 V174 A10 10 0 0 1 174 184 H121 L72 135 V82 A10 10 0 0 1 82 72 Z "
            "M60 140 L116 196 H66 A6 6 0 0 1 60 190 Z "
            "M140 60 H190 A6 6 0 0 1 196 66 V116 Z")
A_VARIANTS["a4"] = f'  <path fill="#000000" fill-rule="evenodd" d="{TILE} {A4_HOLES}"/>\n'

for key, sym in A_VARIANTS.items():
    svg(f"{key}-symbol.svg", 256, sym, f"Lore, photo corners {key.upper()}")
    wx = 224 + GAP
    body = f'  <g id="symbol">\n{sym}  </g>\n  <g id="wordmark" transform="translate({wx} 0)">\n{paths(plain)}  </g>\n'
    svg(f"{key}-lockup.svg", int(wx + w_plain + MARGIN - (32 - MARGIN)), body, f"Lore lockup {key.upper()}", x0=32 - MARGIN)

svg("c-lockup.svg", int(w_ribbon + 64), f'  <g transform="translate(32 0)">\n{paths(ribbon)}  </g>\n', "Lore wordmark, bookmark l")
print("wrote", sorted(f for f in os.listdir(OUT) if f.endswith(".svg")))
