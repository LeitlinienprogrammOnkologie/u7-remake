"""Fit where the world's tiles lie on the painted map (godot/data/map/britannia.png).

usage: python scripts/painted_map_warp.py [--check out.png]

The painting follows Britannia's layout but bends it (towns drawn where they
read well, coasts invented), so Exult's straight Paint_map scale would put the
sextant's mark in the wrong places. The warp is a cubic B-spline on a grid of
control points (tile -> painting pixel), fitted to
  - the towns: the real town (NPC homes, signs) onto its painted picture,
  - the coasts: the game's coastline (u7poster terrain) onto the painting's,
    and real land onto painted land, sea onto sea,
  - smoothness (bending) and no folds (the mark never runs backwards).
It writes godot/data/map/britannia_warp.txt for UI/PaintedMap.cs.
--check draws the warped coastline (red) and the towns (yellow: painted,
magenta: where the warp puts the real town) over the painting.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
import scipy.ndimage as ndi
import scipy.sparse as sp
from PIL import Image, ImageDraw
from scipy.optimize import minimize

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
from u7poster.mapdata import load_map, load_chunks, load_ifix  # noqa: E402
from u7poster.shapes import load_shape_names, ShapeInfo  # noqa: E402
from u7poster.terrain import build_terrain_raster, WATER, VOID  # noqa: E402

PAINTING = ROOT / "godot" / "data" / "map" / "britannia.png"
OUT = ROOT / "godot" / "data" / "map" / "britannia_warp.txt"
TILES = 3072
SPACING = 128.0  # tiles between control points
# Real town (tile, from the median NPC home or the sign) -> its picture on the painting (pixel).
# Yew is left out: the painting draws it some 500 tiles east of the High Court,
# which sits just east of Empath Abbey, so the mark shows Yew by the abbey.
TOWNS = {
    "Empath Abbey": ((240, 300), (182, 118)),
    "Castle Britannia": ((930, 1180), (420, 425)),
    "Britain": ((850, 1380), (372, 525)),
    "Minoc": ((1520, 380), (630, 150)),
    "Cove": ((1570, 1290), (610, 490)),
    "Vesper": ((2140, 1160), (905, 385)),
    "Skara Brae": ((200, 1450), (85, 600)),
    "Paws": ((870, 1750), (360, 665)),
    "Trinsic": ((1025, 2200), (420, 815)),
    "Jhelom": ((420, 2540), (110, 1050)),
    "Buccaneer's Den": ((1610, 1900), (640, 720)),
    "Moonglow": ((2740, 1540), (1130, 610)),
    "New Magincia": ((2200, 2150), (835, 840)),
    "Spektran": ((1832, 2554), (705, 925)),  # Martingo's palace
    "Terfin": ((2120, 2850), (865, 1045)),
    "Serpent's Hold": ((1470, 2770), (590, 1065)),
}
W_TOWN = 50.0
W_COAST = 1.0
W_BEND = 0.2
DET_FLOOR = 0.03  # painting px^2 per tile^2 (about 0.16 on average)


def painted_land(rgb: np.ndarray) -> np.ndarray:
    """The painting's land: not blue sea; grey and white (mountains, foam, clouds) by what surrounds them."""
    rgb = rgb.astype(np.int32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    sat = (rgb.max(-1) - rgb.min(-1)) / np.maximum(rgb.max(-1), 1)
    sea = (b - r > 45) & (b >= g - 10)
    grey = sat < 0.18
    land = (~sea & ~grey) | (grey & (ndi.uniform_filter(sea.astype(float), 11) < 0.45))
    land = ndi.binary_closing(ndi.binary_opening(land, iterations=2), iterations=1)
    lab, n = ndi.label(~land)
    small = np.zeros(n + 1, bool)
    small[1:] = ndi.sum(~land, lab, range(1, n + 1)) < 400
    land |= small[lab]
    land[:22, :] = land[-22:, :] = land[:, :22] = land[:, -22:] = False  # the parchment edge
    return land


def real_land() -> np.ndarray:
    tr = build_terrain_raster(load_shape_names(), ShapeInfo.load(), load_map(), load_chunks(), load_ifix())
    return ndi.binary_opening((tr.cls != WATER) & (tr.cls != VOID), iterations=2)


def bspline(t, d=False):
    if d:
        return np.stack([-(1 - t) ** 2 / 2, (9 * t ** 2 - 12 * t) / 6, (-9 * t ** 2 + 6 * t + 3) / 6, t ** 2 / 2], -1)
    return np.stack([(1 - t) ** 3 / 6, (3 * t ** 3 - 6 * t ** 2 + 4) / 6, (-3 * t ** 3 + 3 * t ** 2 + 3 * t + 1) / 6, t ** 3 / 6], -1)


class Grid:
    """Control point (i, j) stands at tile ((i - 1) * SPACING, (j - 1) * SPACING)."""

    def __init__(self):
        self.n = int(round(TILES / SPACING)) + 3

    def basis(self, pts, dx=False, dy=False):
        n = self.n
        u = np.asarray(pts, float)[:, 0] / SPACING + 1
        v = np.asarray(pts, float)[:, 1] / SPACING + 1
        iu = np.clip(np.floor(u).astype(int), 0, n - 4)
        iv = np.clip(np.floor(v).astype(int), 0, n - 4)
        bu = bspline(u - iu, dx) / (SPACING if dx else 1)
        bv = bspline(v - iv, dy) / (SPACING if dy else 1)
        rows, cols, vals = [], [], []
        for a in range(4):
            for b in range(4):
                rows.append(np.arange(len(u)))
                cols.append((iv + b) * n + iu + a)
                vals.append(bu[:, a] * bv[:, b])
        return sp.csr_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(len(u), n * n))

    def bending(self):
        n = self.n
        idx = np.arange(n * n).reshape(n, n)
        r, c, v = [], [], []
        k = 0
        for j in range(n):
            for i in range(n):
                if 0 < i < n - 1:
                    r += [k] * 3; c += [idx[j, i - 1], idx[j, i], idx[j, i + 1]]; v += [1, -2, 1]; k += 1
                if 0 < j < n - 1:
                    r += [k] * 3; c += [idx[j - 1, i], idx[j, i], idx[j + 1, i]]; v += [1, -2, 1]; k += 1
                if i < n - 1 and j < n - 1:
                    s = np.sqrt(2)
                    r += [k] * 4; c += [idx[j, i], idx[j, i + 1], idx[j + 1, i], idx[j + 1, i + 1]]; v += [s, -s, -s, s]; k += 1
        return sp.csr_matrix((v, (r, c)), shape=(k, n * n))


def fit(land_r: np.ndarray, land_p: np.ndarray) -> tuple[Grid, np.ndarray]:
    H, W = land_p.shape
    grid = Grid()
    src = np.array([t for t, _ in TOWNS.values()], float)
    dst = np.array([p for _, p in TOWNS.values()], float)
    aff = np.linalg.lstsq(np.hstack([src, np.ones((len(src), 1))]), dst, rcond=None)[0]
    ctrl = (np.stack(np.meshgrid(np.arange(grid.n), np.arange(grid.n)), -1).reshape(-1, 2) - 1) * SPACING
    c0 = np.hstack([ctrl, np.ones((len(ctrl), 1))]) @ aff

    coast_p = land_p ^ ndi.binary_erosion(land_p)
    dist = np.minimum(ndi.distance_transform_edt(~coast_p), 30.0)
    dist_gy, dist_gx = np.gradient(dist)
    signed = np.clip(np.where(land_p, -ndi.distance_transform_edt(land_p), ndi.distance_transform_edt(~land_p)), -20, 20)
    signed_gy, signed_gx = np.gradient(signed)

    ys, xs = np.nonzero(land_r ^ ndi.binary_erosion(land_r))
    pick = np.random.default_rng(1).choice(len(xs), min(12000, len(xs)), replace=False)
    b_coast = grid.basis(np.stack([xs[pick] + 0.5, ys[pick] + 0.5], 1))
    gx, gy = np.meshgrid(np.arange(8, TILES, 24), np.arange(8, TILES, 24))
    samples = np.stack([gx.ravel(), gy.ravel()], 1).astype(float)
    on_land = land_r[gy.ravel(), gx.ravel()]
    b_samp, b_dx, b_dy = grid.basis(samples), grid.basis(samples, dx=True), grid.basis(samples, dy=True)
    b_town = grid.basis(src)
    bend = grid.bending()

    def at(field, p):
        return ndi.map_coordinates(field, [np.clip(p[:, 1], 0, H - 1), np.clip(p[:, 0], 0, W - 1)], order=1, mode="nearest")

    def loss(x):
        c = x.reshape(-1, 2)
        grad = np.zeros_like(c)
        r = b_town @ c - dst
        total = W_TOWN * np.mean(np.sum(r ** 2, 1))
        grad += b_town.T @ (2 * W_TOWN * r / len(r))
        p = b_coast @ c
        d = at(dist, p)
        total += W_COAST * np.mean(d ** 2)
        k = 2 * W_COAST * d / len(d)
        grad += b_coast.T @ np.stack([k * at(dist_gx, p), k * at(dist_gy, p)], 1)
        p = b_samp @ c
        s = at(signed, p)
        sign = np.where(on_land, 1.0, -1.0)
        wrong = np.maximum(sign * s + 2, 0)
        total += 0.5 * W_COAST * np.mean(wrong ** 2)
        k = W_COAST * wrong * sign / len(wrong)
        grad += b_samp.T @ np.stack([k * at(signed_gx, p), k * at(signed_gy, p)], 1)
        e = bend @ c
        total += W_BEND * np.sum(e ** 2)
        grad += 2 * W_BEND * (bend.T @ e)
        jx, jy = b_dx @ c, b_dy @ c
        det = jx[:, 0] * jy[:, 1] - jx[:, 1] * jy[:, 0]
        f = np.maximum(DET_FLOOR - det, 0)
        total += 1e5 * np.mean(f ** 2)
        k = -2e5 * f / len(f)
        grad += b_dx.T @ np.stack([k * jy[:, 1], -k * jy[:, 0]], 1)
        grad += b_dy.T @ np.stack([-k * jx[:, 1], k * jx[:, 0]], 1)
        return total, grad.ravel()

    res = minimize(loss, c0.ravel(), jac=True, method="L-BFGS-B", options={"maxiter": 3000, "maxfun": 6000})
    c = res.x.reshape(-1, 2)
    jx, jy = b_dx @ c, b_dy @ c
    det = jx[:, 0] * jy[:, 1] - jx[:, 1] * jy[:, 0]
    off = np.hypot(*(b_town @ c - dst).T)
    print(f"fit: {res.nit} steps; towns off by {off.mean():.1f} px on average, at most {off.max():.1f} "
          f"({list(TOWNS)[int(off.argmax())]}); smallest local scale {det.min():.3f} px^2/tile^2 (folded below 0)")
    return grid, c


def write(grid: Grid, c: np.ndarray, path: Path):
    lines = ["# Where the world's tiles lie on britannia.png: a cubic B-spline warp, tile -> pixel,",
             "# written by scripts/painted_map_warp.py. Control point (i, j) stands at tile",
             "# ((i - 1) * spacing, (j - 1) * spacing); the points follow row by row (j), i within a row.",
             f"spacing {int(SPACING)}", f"size {grid.n}"]
    lines += [f"{x:.2f} {y:.2f}" for x, y in c]
    path.write_text("\n".join(lines) + "\n", encoding="ascii")
    print(f"wrote {path}")


def check(grid: Grid, c: np.ndarray, land_r: np.ndarray, painting: Image.Image, path: Path):
    img = painting.copy()
    px = img.load()
    ys, xs = np.nonzero(land_r ^ ndi.binary_erosion(land_r))
    for x, y in grid.basis(np.stack([xs + 0.5, ys + 0.5], 1)) @ c:
        if 0 <= x < img.width and 0 <= y < img.height:
            px[int(x), int(y)] = (255, 0, 0)
    d = ImageDraw.Draw(img)
    for (tile, (ax, ay)), (mx, my) in zip(TOWNS.values(), grid.basis(np.array([t for t, _ in TOWNS.values()], float)) @ c):
        d.ellipse([ax - 5, ay - 5, ax + 5, ay + 5], outline=(255, 255, 0), width=2)
        d.ellipse([mx - 3, my - 3, mx + 3, my + 3], fill=(255, 0, 255))
    img.save(path)
    print(f"wrote {path}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", type=Path, help="write the warped coastline over the painting here")
    args = ap.parse_args(argv)
    painting = Image.open(PAINTING).convert("RGB")
    land_r = real_land()
    grid, c = fit(land_r, painted_land(np.asarray(painting)))
    write(grid, c, OUT)
    if args.check:
        check(grid, c, land_r, painting, args.check)
    return 0


if __name__ == "__main__":
    sys.exit(main())
