"""Automatic defect detection: data terrain classes vs. coarse_map.png.

1. The tile class raster is projected into coarse pixel space through the
   registration (plus a smooth residual warp interpolated from the block
   shifts, so local distortion of the AI upscale does not read as defects).
2. The coarse image is classified per pixel into water / land / mountain /
   structure with a quadratic discriminant fitted on the image itself: the
   training pixels are those the data calls a class (eroded by the boundary
   tolerance), features are RGB, local mean and local contrast.
3. Class-by-class disagreement masks are opened, labelled and filtered by
   size; each blob becomes a FixLog row.
4. Texture repetition (autocorrelation peaks per window) and suspected
   invented text (rows of small high-contrast glyph-like strokes) are
   flagged as separate classes.
"""
from __future__ import annotations
from dataclasses import dataclass, field

import numpy as np
import scipy.ndimage as ndi
from scipy.interpolate import griddata

from .config import CONFIG
from .registration import Registration, coarse_land_mask
from .terrain import WATER, VOID, MOUNTAIN, STRUCTURE, DOCK, ROAD, SAND, SWAMP, CAVE, FOREST, LAND

# merged classes for the comparison
C_WATER, C_LAND, C_MOUNT, C_STRUCT = 0, 1, 2, 3
MERGE = {VOID: C_WATER, WATER: C_WATER, LAND: C_LAND, SAND: C_LAND, SWAMP: C_LAND, CAVE: C_LAND, ROAD: C_LAND,
         FOREST: C_LAND, DOCK: C_STRUCT, STRUCTURE: C_STRUCT, MOUNTAIN: C_MOUNT}
CLASS_LABEL = {C_WATER: "water", C_LAND: "land", C_MOUNT: "mountain", C_STRUCT: "structure"}


@dataclass
class Defect:
    id: str
    dclass: str          # water-land / river / road / mountain / structure / texture / text
    cx: int
    cy: int
    w: int
    h: int
    area: int
    issue: str
    prompt: str
    tile_x: int = 0
    tile_y: int = 0
    extra: dict = field(default_factory=dict)

    @property
    def box(self):
        return max(self.w, self.h)


def residual_field(reg: Registration, size: int, grid: int = 64):
    """Dense residual shift field (dx, dy) in coarse px from the inlier block shifts."""
    pts = [(cx, cy, sx, sy) for (cx, cy, sx, sy, iou, ok) in reg.block_shifts if ok]
    if len(pts) < 4:
        return None
    P = np.array(pts)
    # residual of each block under the linear model
    tx = (P[:, 0] - reg.offset_x) / reg.scale_x
    ty = (P[:, 1] - reg.offset_y) / reg.scale_y
    # block matcher convention: data at (u+sx, v+sy) appears at coarse (u, v)
    rx = (P[:, 0] - P[:, 2]) - P[:, 0]
    ry = (P[:, 1] - P[:, 3]) - P[:, 1]
    ys, xs = np.mgrid[0:size:grid, 0:size:grid]
    q = np.stack([xs.ravel(), ys.ravel()], 1)
    fx = griddata(P[:, :2], -rx, q, method="linear")
    fy = griddata(P[:, :2], -ry, q, method="linear")
    nx = griddata(P[:, :2], -rx, q, method="nearest")
    ny = griddata(P[:, :2], -ry, q, method="nearest")
    fx = np.where(np.isnan(fx), nx, fx).reshape(xs.shape)
    fy = np.where(np.isnan(fy), ny, fy).reshape(xs.shape)
    fx = ndi.zoom(fx, size / fx.shape[0], order=1)[:size, :size]
    fy = ndi.zoom(fy, size / fy.shape[0], order=1)[:size, :size]
    return fx.astype(np.float32), fy.astype(np.float32)


def project_classes(cls: np.ndarray, reg: Registration, size: int, warp=None) -> np.ndarray:
    """Merged data class per coarse pixel (uint8, 255 = outside the map)."""
    merged = np.full(256, 255, np.uint8)
    for k, v in MERGE.items():
        merged[k] = v
    v, u = np.mgrid[0:size, 0:size].astype(np.float32)
    if warp is not None:
        u = u + warp[0]
        v = v + warp[1]
    tx = np.floor((u - reg.offset_x) / reg.scale_x).astype(np.int64)
    ty = np.floor((v - reg.offset_y) / reg.scale_y).astype(np.int64)
    ok = (tx >= 0) & (tx < cls.shape[1]) & (ty >= 0) & (ty < cls.shape[0])
    out = np.full((size, size), 255, np.uint8)
    out[ok] = merged[cls[ty[ok], tx[ok]]]
    return out


def project_raster(r: np.ndarray, reg: Registration, size: int, warp=None, fill=0):
    v, u = np.mgrid[0:size, 0:size].astype(np.float32)
    if warp is not None:
        u = u + warp[0]
        v = v + warp[1]
    tx = np.floor((u - reg.offset_x) / reg.scale_x).astype(np.int64)
    ty = np.floor((v - reg.offset_y) / reg.scale_y).astype(np.int64)
    ok = (tx >= 0) & (tx < r.shape[1]) & (ty >= 0) & (ty < r.shape[0])
    out = np.full((size, size) + r.shape[2:], fill, r.dtype)
    out[ok] = r[ty[ok], tx[ok]]
    return out


def image_features(rgb: np.ndarray) -> np.ndarray:
    g = rgb.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    mean15 = ndi.uniform_filter(g, 15)
    dev = ndi.uniform_filter(np.abs(g - ndi.uniform_filter(g, 5)), 15)
    dark = ndi.uniform_filter((g < mean15 - 40).astype(np.float32), 15)   # density of dark strokes
    f = np.stack([rgb[..., 0].astype(np.float32), rgb[..., 1].astype(np.float32), rgb[..., 2].astype(np.float32),
                  mean15, dev, dark * 255], -1)
    return f


def classify_image(feat: np.ndarray, data_cls: np.ndarray, erode: int, log=print, samples: int = 150000):
    """QDA with class models fitted on pixels whose data class is unambiguous."""
    H, W, F = feat.shape
    rng = np.random.default_rng(0)
    models = {}
    for c in (C_WATER, C_LAND, C_MOUNT, C_STRUCT):
        m = data_cls == c
        if erode:
            m = ndi.binary_erosion(m, iterations=erode)
        idx = np.flatnonzero(m)
        if len(idx) < 500:
            log(f"defects: class {CLASS_LABEL[c]} has too few training pixels ({len(idx)})")
            continue
        if len(idx) > samples:
            idx = rng.choice(idx, samples, replace=False)
        x = feat.reshape(-1, F)[idx]
        mu = x.mean(0)
        cov = np.cov(x.T) + np.eye(F) * 1.0
        models[c] = (mu, np.linalg.inv(cov), np.log(np.linalg.det(cov)), np.log(len(idx) / m.size))
    X = feat.reshape(-1, F)
    best = np.full(X.shape[0], -1e30, np.float32)
    pred = np.zeros(X.shape[0], np.uint8)
    for c, (mu, icov, logdet, prior) in models.items():
        d = X - mu
        ll = -0.5 * np.einsum("ij,jk,ik->i", d, icov, d) - 0.5 * logdet + prior
        better = ll > best
        best[better] = ll[better]
        pred[better] = c
    return pred.reshape(H, W)


def _blobs(mask: np.ndarray, open_px: int, min_blob: int, min_area: int):
    if open_px > 0:
        mask = ndi.binary_opening(mask, structure=np.ones((3, 3)), iterations=open_px)
    lab, n = ndi.label(mask)
    if n == 0:
        return []
    out = []
    for i, sl in enumerate(ndi.find_objects(lab), 1):
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if max(w, h) < min_blob:
            continue
        m = lab[sl] == i
        area = int(m.sum())
        if area < min_area:
            continue
        ys, xs = np.nonzero(m)
        out.append(dict(cx=int(xs.mean()) + sl[1].start, cy=int(ys.mean()) + sl[0].start, w=int(w), h=int(h),
                        x0=sl[1].start, y0=sl[0].start, area=area, mask=m))
    return out


def _fraction_in(mask_pred: np.ndarray, blob) -> float:
    sub = mask_pred[blob["y0"]:blob["y0"] + blob["h"], blob["x0"]:blob["x0"] + blob["w"]]
    return float(sub[blob["mask"]].mean()) if blob["area"] else 0.0


def find_defects(cls: np.ndarray, coarse_rgb: np.ndarray, reg: Registration, cfg=CONFIG, log=print):
    N = coarse_rgb.shape[0]
    tol = int(cfg["defect_boundary_erode_px"])
    open_px = int(cfg["defect_open_px"])
    min_blob = int(cfg["defect_min_blob_px"])
    min_area = int(cfg["defect_min_area_px"])
    warp = residual_field(reg, N)
    dcls = project_classes(cls, reg, N, warp)
    road_d = project_raster((cls == ROAD).astype(np.uint8), reg, N, warp).astype(bool)
    inside = dcls != 255
    log(f"defects: projected data classes ({'with' if warp is not None else 'without'} residual warp)")

    feat = image_features(coarse_rgb)
    pred = classify_image(feat, dcls, tol, log)
    land_c = coarse_land_mask(coarse_rgb)
    pred_water = ~land_c
    pred_land = land_c
    pred_mount = pred == C_MOUNT
    pred_struct = pred == C_STRUCT
    # a road in the drawing: thin dark line on land -> use stroke density feature within land
    stroke = feat[..., 5] / 255.0
    pred_road = (stroke > 0.12) & land_c

    defects: list[Defect] = []
    masks = {}

    def erode(m, it):
        return ndi.binary_erosion(m, iterations=it) if it else m

    # --- water vs land ---
    d_water = (dcls == C_WATER) & inside
    d_land = (dcls != C_WATER) & inside
    # both sides eroded by the tolerance: only confident disagreements survive
    wl1 = erode(d_water, tol) & erode(pred_land, tol)
    wl2 = erode(d_land, tol) & erode(pred_water, tol)
    masks["water-land"] = np.zeros_like(wl1)
    for b in _blobs(wl1, open_px, min_blob, min_area):
        masks["water-land"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]
        b["issue"] = f"coastline deviates: data has water, image shows land (~{b['w']}x{b['h']} px)"
        b["prompt"] = "open sea / water in the same parchment style as the surrounding ocean, remove the land here"
        b["cls"] = "water-land"
        defects.append(b)
    for b in _blobs(wl2, open_px, min_blob, min_area):
        masks["water-land"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]
        b["issue"] = f"coastline deviates: data has land, image shows water (~{b['w']}x{b['h']} px)"
        b["prompt"] = "extend the coastline: hatched land with shoreline matching the neighbouring terrain"
        b["cls"] = "water-land"
        defects.append(b)

    # --- rivers / lakes: inland water in the data ---
    wat = d_water
    ocean_lab, n = ndi.label(ndi.binary_opening(wat, iterations=2))
    if n:
        sizes = ndi.sum(wat, ocean_lab, range(1, n + 1))
        ocean = ocean_lab == (np.argmax(sizes) + 1)
        ocean = ndi.binary_dilation(ocean, iterations=3)
    else:
        ocean = np.zeros_like(wat)
    inland = wat & ~ocean
    masks["river"] = np.zeros_like(wat)
    for b in _blobs(inland, 0, min_blob, min_area):
        frac = _fraction_in(pred_water, b)
        if frac < cfg["river_min_fraction"]:
            kind = "lake" if b["area"] > 4 * max(b["w"], b["h"]) else "river"
            b["issue"] = f"{kind} missing: only {frac:.0%} of the water body reads as water in the image"
            b["prompt"] = f"draw a {kind} here in the map's ink style (blue-grey wash with a dark outline), following the data reference"
            b["cls"] = "river"
            defects.append(b)
            masks["river"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]

    # --- roads ---
    masks["road"] = np.zeros_like(wat)
    road_seg = ndi.binary_dilation(road_d & inside, iterations=2)
    for b in _blobs(road_seg, 0, 24, 200):
        frac = _fraction_in(ndi.binary_dilation(pred_road, iterations=4), b)
        if frac < cfg["road_min_fraction"]:
            b["issue"] = f"road/path missing: only {frac:.0%} of the road tiles have a drawn line ({b['w']}x{b['h']} px)"
            b["prompt"] = "draw the road as a thin dashed ink line following the reference, same weight as other roads"
            b["cls"] = "road"
            defects.append(b)
            masks["road"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]

    # --- mountains ---
    d_mount = (dcls == C_MOUNT)
    m1 = erode(d_mount, tol) & erode(~pred_mount & ~pred_water, tol)
    m2 = erode(pred_mount & inside, tol) & erode(dcls == C_LAND, tol)
    masks["mountain"] = np.zeros_like(m1)
    for b in _blobs(m1, open_px, min_blob * 2, min_area * 3):
        masks["mountain"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]
        frac = _fraction_in(pred_mount, b)
        b["issue"] = f"mountains missing: data has mountains, image reads as flat land ({frac:.0%} mountain, {b['w']}x{b['h']} px)"
        b["prompt"] = "add hatched mountain ridges in the map's style, matching the neighbouring range"
        b["cls"] = "mountain"
        defects.append(b)
    for b in _blobs(m2, open_px, min_blob * 2, min_area * 3):
        masks["mountain"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]
        b["issue"] = f"mountains invented: image shows ridges where the data has flat land ({b['w']}x{b['h']} px)"
        b["prompt"] = "replace the ridges with flat hatched land / forest matching the surroundings"
        b["cls"] = "mountain"
        defects.append(b)

    # --- structures ---
    d_struct = (dcls == C_STRUCT)
    masks["structure"] = np.zeros_like(wat)
    for b in _blobs(d_struct, 0, min_blob, min_area):
        frac = _fraction_in(ndi.binary_dilation(pred_struct, iterations=tol), b)
        if frac < cfg["structure_min_fraction"]:
            b["issue"] = f"structure present in data but not in image ({frac:.0%} covered, {b['w']}x{b['h']} px)"
            b["prompt"] = "draw the building / wall footprint here as small dark blocks in the map's style"
            b["cls"] = "structure"
            defects.append(b)
            masks["structure"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]
    s2 = erode(pred_struct & inside, max(tol // 2, 1)) & ~ndi.binary_dilation(d_struct, iterations=tol * 2) & ~pred_water
    for b in _blobs(s2, open_px, min_blob * 2, min_area * 4):
        b["issue"] = f"structure invented: dark block-like drawing with no building in the data ({b['w']}x{b['h']} px)"
        b["prompt"] = "remove the building-like marks and continue the surrounding terrain texture"
        b["cls"] = "structure"
        defects.append(b)
        masks["structure"][b["y0"]:b["y0"] + b["h"], b["x0"]:b["x0"] + b["w"]] |= b["mask"]

    # --- texture repetition ---
    gray = feat[..., 3]
    win = int(cfg["texture_window_px"])
    step = win // 2
    minlag = int(cfg["texture_min_lag_px"])
    thr = cfg["texture_corr_threshold"]
    tex = []
    for y0 in range(0, N - win + 1, step):
        for x0 in range(0, N - win + 1, step):
            g = gray[y0:y0 + win, x0:x0 + win] - gray[y0:y0 + win, x0:x0 + win].mean()
            if g.std() < 4:
                continue
            F = np.fft.rfft2(g)
            ac = np.fft.irfft2(F * np.conj(F), s=g.shape)
            ac = np.fft.fftshift(ac) / ac.flat[0]
            c = win // 2
            # secondary peaks only: local maxima outside the central lobe that stand
            # clearly above the correlation half-way back towards the origin
            loc = (ac == ndi.maximum_filter(ac, size=15)) & (ac > thr)
            yy, xx = np.nonzero(loc)
            best = None
            for iy, ix in zip(yy, xx):
                lx, ly = ix - c, iy - c
                if max(abs(lx), abs(ly)) < minlag or lx < 0 or (lx == 0 and ly <= 0):
                    continue
                mid = ac[c + ly // 2, c + lx // 2]
                if ac[iy, ix] - mid > 0.12 and (best is None or ac[iy, ix] > best[0]):
                    best = (float(ac[iy, ix]), lx, ly)
            if best:
                tex.append((x0, y0, best[0], int(best[1]), int(best[2])))
    masks["texture"] = np.zeros_like(wat)
    for x0, y0, pk, lx, ly in tex:
        b = dict(cx=x0 + win // 2, cy=y0 + win // 2, w=win, h=win, x0=x0, y0=y0, area=win * win, mask=None,
                 issue=f"texture repetition: autocorrelation peak {pk:.2f} at lag ({lx},{ly}) px",
                 prompt="regenerate this tile with varied, non-repeating hatching", cls="texture")
        defects.append(b)
        masks["texture"][y0:y0 + win, x0:x0 + win] = True

    # --- suspected text ---
    g = feat[..., 3]
    gray_full = coarse_rgb.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    dark = gray_full < ndi.uniform_filter(gray_full, 31) - 55
    lab, n = ndi.label(dark)
    twin = int(cfg["text_window_px"])
    masks["text"] = np.zeros_like(wat)
    if n:
        objs = ndi.find_objects(lab)
        sizes = ndi.sum(dark, lab, range(1, n + 1))
        cand = []
        for i, sl in enumerate(objs):
            h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
            fill = sizes[i] / (w * h)
            if 5 <= h <= 26 and 2 <= w <= 26 and 6 <= sizes[i] <= 300 and 0.15 < fill < 0.6:
                cand.append(((sl[1].start + sl[1].stop) / 2, (sl[0].start + sl[0].stop) / 2, h))
        if cand:
            C = np.array(cand)
            gx = (C[:, 0] // twin).astype(int)
            gy = (C[:, 1] // twin).astype(int)
            key = gy * 100000 + gx
            density = {}
            for k in np.unique(key):
                density[int(k)] = int((key == k).sum())
            for k in np.unique(key):
                sel = C[key == k]
                if len(sel) < cfg["text_min_glyphs"]:
                    continue
                # isolation: a text line is not embedded in a field of similar strokes
                gxk, gyk = int(k % 100000), int(k // 100000)
                around = [density.get((gyk + dy) * 100000 + (gxk + dx), 0) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dx or dy)]
                if np.mean(around) > 0.5 * len(sel):
                    continue
                # glyphs of a text line share a baseline: look for >= min_glyphs within +-4 px in y and similar height
                ys = np.sort(sel[:, 1])
                best = 0
                for y in ys:
                    row = sel[np.abs(sel[:, 1] - y) <= 4]
                    if len(row) >= 4 and np.std(row[:, 2]) < 4:
                        xs = np.sort(row[:, 0])
                        gaps = np.diff(xs)
                        med = np.median(gaps) if len(gaps) else 0
                        if 4 <= med <= 30 and np.std(gaps) < 0.6 * med:
                            best = max(best, len(row))
                if best >= cfg["text_min_glyphs"]:
                    x0, y0 = int((k % 100000) * twin), int((k // 100000) * twin)
                    if not land_c[y0:y0 + twin, x0:x0 + twin].mean() > 0.6:   # text over sea/parchment is most suspicious
                        pass
                    b = dict(cx=x0 + twin // 2, cy=y0 + twin // 2, w=twin, h=twin, x0=x0, y0=y0, area=twin * twin, mask=None,
                             issue=f"suspected invented text: {best} aligned glyph-like strokes",
                             prompt="remove the lettering and restore the underlying terrain texture", cls="text")
                    defects.append(b)
                    masks["text"][y0:y0 + twin, x0:x0 + twin] = True

    # build rows sorted by box size
    defects.sort(key=lambda b: -max(b["w"], b["h"]))
    rows = []
    for i, b in enumerate(defects, 1):
        tx, ty = reg.px_to_tile(b["cx"], b["cy"])
        rows.append(Defect(f"F{i:03d}", b["cls"], b["cx"], b["cy"], b["w"], b["h"], b["area"], b["issue"], b["prompt"],
                           int(tx), int(ty)))
    counts = {}
    for r in rows:
        counts[r.dclass] = counts.get(r.dclass, 0) + 1
    log("defects: " + ", ".join(f"{k}={v}" for k, v in sorted(counts.items())) + f" (total {len(rows)})")
    return rows, masks, dict(pred=pred, dcls=dcls, land_c=land_c, pred_road=pred_road, warp=warp)
