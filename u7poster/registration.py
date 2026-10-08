"""Align coarse_map.png with the tile data.

Model: coarse_px_x = offset_x + tile_x * scale_x (same for y). Fitted by
(1) global phase correlation of a land/sea mask derived from the coarse
image (local texture + darkness, Otsu threshold) against the data land mask,
then (2) block-wise phase correlation and a least-squares linear fit of the
block shifts, iterated. Residual = RMS of block shifts after the fit.
"""
from __future__ import annotations
from dataclasses import dataclass

import numpy as np
import scipy.ndimage as ndi
from PIL import Image

from .terrain import WATER, VOID


@dataclass
class Registration:
    offset_x: float
    offset_y: float
    scale_x: float
    scale_y: float
    residual_px: float
    block_shifts: list

    def tile_to_px(self, tx, ty):
        return self.offset_x + np.asarray(tx) * self.scale_x, self.offset_y + np.asarray(ty) * self.scale_y

    def px_to_tile(self, px, py):
        return (np.asarray(px) - self.offset_x) / self.scale_x, (np.asarray(py) - self.offset_y) / self.scale_y


def load_coarse(path) -> np.ndarray:
    Image.MAX_IMAGE_PIXELS = None
    return np.asarray(Image.open(path).convert("RGB"))


def coarse_land_score(rgb: np.ndarray) -> np.ndarray:
    """Texture + darkness score in [0,1]; high where the drawing is busy/dark (land)."""
    g = rgb.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    local = ndi.uniform_filter(g, 9)
    dev = ndi.uniform_filter(np.abs(g - local), 21)
    dark = ndi.uniform_filter(255.0 - g, 21)
    s = dev / max(np.percentile(dev, 99), 1e-6) + 0.5 * dark / max(np.percentile(dark, 99), 1e-6)
    return np.clip(s / np.percentile(s, 99.5), 0, 1)


def otsu(x: np.ndarray) -> float:
    h, edges = np.histogram(x.ravel(), 256, (0, 1))
    h = h.astype(np.float64)
    c = edges[:-1] + np.diff(edges) / 2
    w1 = np.cumsum(h)
    w2 = w1[-1] - w1
    m1 = np.cumsum(h * c) / np.maximum(w1, 1)
    m2 = (np.cumsum((h * c)[::-1])[::-1]) / np.maximum(w2, 1)
    var = w1 * w2 * (m1 - m2) ** 2
    return float(c[np.argmax(var)])


def coarse_land_mask(rgb: np.ndarray) -> np.ndarray:
    s = coarse_land_score(rgb)
    t = otsu(s)
    m = s > t
    m = ndi.binary_opening(m, iterations=2)
    m = ndi.binary_closing(m, iterations=2)
    return m


def resample_data(mask_d: np.ndarray, reg: Registration, size: int) -> np.ndarray:
    """Nearest-neighbour lookup of a tile raster in coarse pixel space (outside -> 0)."""
    u = np.arange(size)
    tx = np.floor((u - reg.offset_x) / reg.scale_x).astype(np.int64)
    ty = np.floor((u - reg.offset_y) / reg.scale_y).astype(np.int64)
    okx = (tx >= 0) & (tx < mask_d.shape[1])
    oky = (ty >= 0) & (ty < mask_d.shape[0])
    out = np.zeros((size, size), mask_d.dtype)
    sub = mask_d[np.clip(ty, 0, mask_d.shape[0] - 1)][:, np.clip(tx, 0, mask_d.shape[1] - 1)]
    out[:] = sub
    out[~oky, :] = 0
    out[:, ~okx] = 0
    return out


def phase_corr(a: np.ndarray, b: np.ndarray, max_shift: int | None = None):
    """Shift (dx, dy) such that b ~= a shifted by (dx, dy). Returns (dx, dy, peak)."""
    A = np.fft.rfft2(a - a.mean())
    B = np.fft.rfft2(b - b.mean())
    R = A * np.conj(B)
    R /= np.maximum(np.abs(R), 1e-9)
    r = np.fft.irfft2(R, s=a.shape)
    if max_shift:
        m = np.zeros_like(r, bool)
        m[:max_shift + 1, :max_shift + 1] = True
        m[-max_shift:, :max_shift + 1] = True
        m[:max_shift + 1, -max_shift:] = True
        m[-max_shift:, -max_shift:] = True
        r = np.where(m, r, -np.inf)
    iy, ix = np.unravel_index(np.argmax(r), r.shape)
    dy = iy if iy <= a.shape[0] // 2 else iy - a.shape[0]
    dx = ix if ix <= a.shape[1] // 2 else ix - a.shape[1]
    return -dx, -dy, float(r[iy, ix])


def fill_small_holes(m: np.ndarray, max_frac: float = 0.01) -> np.ndarray:
    """Fill enclosed non-land regions smaller than max_frac of the image (lakes, rivers, swamp mosaics)."""
    inv = ~m
    lab, n = ndi.label(inv)
    if n == 0:
        return m
    sizes = ndi.sum(inv, lab, range(1, n + 1))
    small = np.zeros(n + 1, bool)
    small[1:] = sizes < max_frac * m.size
    small[0] = False
    return m | small[lab]


def block_shift_iou(a: np.ndarray, b: np.ndarray, rng: int, step: int = 1):
    """Shift (dx, dy) of b relative to a maximising the overlap of two binary blocks."""
    best, bdx, bdy = -1.0, 0, 0
    H, W = a.shape
    for dy in range(-rng, rng + 1, step):
        for dx in range(-rng, rng + 1, step):
            ya0, ya1 = max(0, dy), min(H, H + dy)
            xa0, xa1 = max(0, dx), min(W, W + dx)
            sa = a[ya0:ya1, xa0:xa1]
            sb = b[ya0 - dy:ya1 - dy, xa0 - dx:xa1 - dx]
            inter = np.count_nonzero(sa & sb)
            union = np.count_nonzero(sa | sb)
            iou = inter / union if union else 0.0
            if iou > best:
                best, bdx, bdy = iou, dx, dy
    return bdx, bdy, best


def register(cls: np.ndarray, coarse_rgb: np.ndarray, blocks: int = 6, iters: int = 3, log=print) -> tuple[Registration, np.ndarray]:
    N = coarse_rgb.shape[0]
    T = cls.shape[0]
    land_d_raw = (cls != WATER) & (cls != VOID)
    land_c = coarse_land_mask(coarse_rgb)
    land_d = fill_small_holes(land_d_raw, 0.004)
    lc = fill_small_holes(land_c, 0.004)
    reg = Registration(0.0, 0.0, N / T, N / T, 0.0, [])
    d = resample_data(land_d.astype(np.float32), reg, N)
    dx, dy, pk = phase_corr(d, lc.astype(np.float32), max_shift=N // 8)
    reg.offset_x += dx
    reg.offset_y += dy
    log(f"registration: global shift dx={dx} dy={dy}, initial scale {N / T:.4f} px/tile")
    for it in range(iters):
        d = resample_data(land_d.astype(np.uint8), reg, N).astype(bool)
        bs = N // blocks
        pts = []
        for by in range(blocks):
            for bx in range(blocks):
                sl = (slice(by * bs, (by + 1) * bs), slice(bx * bs, (bx + 1) * bs))
                a, b = d[sl], lc[sl]
                edge = np.count_nonzero(a ^ ndi.binary_erosion(a, iterations=3)) / a.size
                if edge < 0.02:
                    continue
                # coarse-to-fine search: 1/4 res +-16 (=64 px), then full res +-4
                a4 = a[::4, ::4]
                b4 = b[::4, ::4]
                sx, sy, _ = block_shift_iou(a4, b4, 16, 1)
                sx, sy = sx * 4, sy * 4
                # refine around the coarse optimum
                best = (-1.0, sx, sy)
                for ddy in range(-4, 5, 2):
                    for ddx in range(-4, 5, 2):
                        ex, ey = sx + ddx, sy + ddy
                        ya0, ya1 = max(0, ey), min(bs, bs + ey)
                        xa0, xa1 = max(0, ex), min(bs, bs + ex)
                        sa = a[ya0:ya1, xa0:xa1]
                        sb = b[ya0 - ey:ya1 - ey, xa0 - ex:xa1 - ex]
                        u = np.count_nonzero(sa | sb)
                        iou = np.count_nonzero(sa & sb) / u if u else 0
                        if iou > best[0]:
                            best = (iou, ex, ey)
                iou, sx, sy = best
                cx, cy = bx * bs + bs / 2, by * bs + bs / 2
                pts.append((cx, cy, sx, sy, iou))
        if len(pts) < 4:
            log("registration: too few coastline blocks for a scale fit; keeping global shift")
            break
        P = np.array(pts)
        keep = np.ones(len(P), bool)
        for _ in range(3):
            Q = P[keep]
            tx = (Q[:, 0] - reg.offset_x) / reg.scale_x
            ty = (Q[:, 1] - reg.offset_y) / reg.scale_y
            ux, uy = Q[:, 0] - Q[:, 2], Q[:, 1] - Q[:, 3]
            w = np.clip(Q[:, 4], 0.05, None)
            Ax = np.stack([np.ones_like(tx), tx], 1)
            Ay = np.stack([np.ones_like(ty), ty], 1)
            sol_x = np.linalg.lstsq(Ax * w[:, None], ux * w, rcond=None)[0]
            sol_y = np.linalg.lstsq(Ay * w[:, None], uy * w, rcond=None)[0]
            # residuals for all blocks under the new model
            txa = (P[:, 0] - reg.offset_x) / reg.scale_x
            tya = (P[:, 1] - reg.offset_y) / reg.scale_y
            rx = sol_x[0] + sol_x[1] * txa - (P[:, 0] - P[:, 2])
            ry = sol_y[0] + sol_y[1] * tya - (P[:, 1] - P[:, 3])
            r = np.hypot(rx, ry)
            med = np.median(r[keep])
            keep = r <= max(2.5 * med, 6.0)
        res = float(np.sqrt(np.mean(r[keep] ** 2)))
        reg = Registration(float(sol_x[0]), float(sol_y[0]), float(sol_x[1]), float(sol_y[1]), res,
                           [(float(a), float(b), float(c), float(e), float(f), bool(k)) for (a, b, c, e, f), k in zip(pts, keep)])
        log(f"registration: iter {it + 1}: offset=({reg.offset_x:.1f},{reg.offset_y:.1f}) scale=({reg.scale_x:.4f},{reg.scale_y:.4f}) "
            f"blocks={int(keep.sum())}/{len(pts)} inliers, residual={res:.1f}px (max inlier {r[keep].max():.0f}px, "
            f"outliers {[int(v) for v in r[~keep]]})")
    return reg, land_c


def registration_check_image(cls, coarse_rgb, reg: Registration, land_c, out_size=1024) -> Image.Image:
    N = coarse_rgb.shape[0]
    d = resample_data(((cls != WATER) & (cls != VOID)).astype(np.uint8), reg, N).astype(bool)
    edge_d = d ^ ndi.binary_erosion(d, iterations=2)
    edge_c = land_c ^ ndi.binary_erosion(land_c, iterations=2)
    img = coarse_rgb.copy()
    img[edge_c] = (0, 90, 255)
    img[edge_d] = (255, 0, 0)
    return Image.fromarray(img).resize((out_size, out_size), Image.BOX)
