"""Compare two shots (the console's `shot`, the suite's shots) and show where they differ.

usage: python scripts/agent/shot_diff.py <a.png> <b.png> [out.png]

Prints how many pixels differ, the mean and largest difference (8-bit
levels, the largest channel of each pixel), and writes out.png: a beside b
above a heat map of the difference, amplified four times (docs/RENDERER.md,
section 4: modern mode is approved from shots and difference images against
classic). Exit 1 if the sizes differ.
"""
import sys

import numpy as np
from PIL import Image


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    a = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(np.int32)
    b = np.asarray(Image.open(sys.argv[2]).convert('RGB')).astype(np.int32)
    if a.shape != b.shape:
        sys.exit(f'sizes differ: {a.shape[1]}x{a.shape[0]} against {b.shape[1]}x{b.shape[0]}')
    d = np.abs(a - b).max(axis=2)
    differing = int((d > 0).sum())
    print(f'{differing} of {d.size} pixels differ ({100 * differing / d.size:.1f}%), mean {d.mean():.2f}, largest {int(d.max())} levels'
          f'{"" if differing == 0 else f", mean over the differing {d[d > 0].mean():.2f}"}')
    if len(sys.argv) > 3:
        heat = np.clip(d * 4, 0, 255).astype(np.uint8)
        heat = np.stack([heat, heat // 2, np.zeros_like(heat)], axis=2)
        top = np.concatenate([a, b], axis=1).astype(np.uint8)
        bottom = np.concatenate([heat, np.zeros_like(heat)], axis=1)
        Image.fromarray(np.concatenate([top, bottom], axis=0)).save(sys.argv[3])
        print(f'wrote {sys.argv[3]}')


if __name__ == '__main__':
    main()
