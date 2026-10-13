"""Does a plane dump's water cycle at the art's resolution? (docs/RENDERER.md, R4)

usage: python scripts/agent/cycling_check.py <dump name> [scale]

Reads <agent>/planes/<name>/index.png (the console's `art dump`) and, at
the art's scale (4 by default), takes every scale x scale block whose
indices all lie in one of Exult's colour-cycling ranges (rotatecolours:
0xE0-0xE7, 0xE8-0xEF, 0xF0-0xF3, 0xF4-0xF7, 0xF8-0xFB, 0xFC-0xFE), i.e. a
cycling pixel of the original blown up or painted anew. Prints how many
such blocks there are and how many hold more than one index: with the
original art blown up, none do; with version 3 art whose cycling pixels are
quantised within their range, most do, and they cycle in the palette's
rotation texel by texel. Exit 1 if no block varies.
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import agent  # noqa: E402

RANGES = [(0xFC, 3), (0xF8, 4), (0xF4, 4), (0xF0, 4), (0xE8, 8), (0xE0, 8)]


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    name = sys.argv[1]
    scale = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    index = np.asarray(Image.open(os.path.join(agent.DIR, 'planes', name, 'index.png')).convert('L'))
    h, w = index.shape
    blocks = index[:h // scale * scale, :w // scale * scale].reshape(h // scale, scale, w // scale, scale).transpose(0, 2, 1, 3)
    blocks = blocks.reshape(-1, scale * scale)
    rng = np.full(blocks.shape, -1, np.int32)
    for k, (first, count) in enumerate(RANGES):
        rng[(blocks >= first) & (blocks < first + count)] = k
    cycling = (rng[:, :1] >= 0).ravel() & (rng == rng[:, :1]).all(axis=1)
    varied = cycling & (blocks != blocks[:, :1]).any(axis=1)
    print(f'{name}: {cycling.sum()} cycling blocks of {scale}x{scale}, {varied.sum()} with more than one index')
    sys.exit(0 if varied.any() else 1)


if __name__ == '__main__':
    main()
