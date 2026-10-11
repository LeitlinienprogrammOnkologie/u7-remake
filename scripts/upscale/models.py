"""The upscaling models: where to get them, converting them to ONNX once
(spandrel on CPU torch), and running them on the GPU (ONNX Runtime with
DirectML, which works on AMD and NVIDIA cards alike under Windows).

Nothing here is redistributed: each player downloads the models into
assets/hires/models. Their licences are the authors' (listed below).
"""

from __future__ import annotations

import urllib.request
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from u7art import HIRES

MODELS_DIR = HIRES / "models"


@dataclass(frozen=True)
class Model:
    name: str
    file: str
    url: str
    licence: str
    note: str


MODELS = {m.name: m for m in [
    Model("realesrgan", "RealESRGAN_x4plus.pth",
          "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth",
          "BSD-3-Clause", "Real-ESRGAN x4plus: general, photographic"),
    Model("realesrgan-anime", "RealESRGAN_x4plus_anime_6B.pth",
          "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.2.4/RealESRGAN_x4plus_anime_6B.pth",
          "BSD-3-Clause", "Real-ESRGAN x4plus anime 6B: clean lines, flat colour"),
    Model("nxbrz", "4x-NXbrz.pth",
          "https://objectstorage.us-phoenix-1.oraclecloud.com/n/ax6ygfvpvzka/b/open-modeldb-files/o/4x-NXbrz.pth",
          "CC-BY-NC-SA-4.0 (Archerpolation)", "pixel art, trained on xBRZ and ScaleNX (Exult's 4xBR family)"),
    Model("rebout", "4x-Rebout-Blend.pth",
          "https://drive.google.com/uc?export=download&confirm=1&id=1h-bY4gq_61sRF_xK3F4OiVK1cWRX0Q9w",
          "CC-BY-NC-SA-4.0 (LyonHrt)", "pixel art sprites with gradient blending"),
    Model("lollypop", "4x-Lollypop.pth",
          "https://drive.google.com/uc?export=download&confirm=1&id=10h8YXKKOQ61ANnwLjjHqXJdn4SbBuUku",
          "CC-BY-NC-SA-4.0 (rapidfire)", "general, pre-rendered art, pixel art and dedithering"),
]}


def ensure_onnx(model: Model) -> Path:
    """The model as ONNX (dynamic height and width), downloaded and converted when missing."""
    MODELS_DIR.mkdir(parents=True, exist_ok=True)
    pth = MODELS_DIR / model.file
    onnx_path = pth.with_suffix(".onnx")
    if onnx_path.exists():
        return onnx_path
    if not pth.exists():
        print(f"downloading {model.name} ({model.licence}) ...", flush=True)
        urllib.request.urlretrieve(model.url, pth)

    import torch
    import spandrel

    desc = spandrel.ModelLoader().load_from_file(str(pth))
    if desc.scale != 4:
        raise SystemExit(f"{model.name} is {desc.scale}x, not 4x")
    net = desc.model.eval()
    dummy = torch.rand(1, 3, 64, 64)
    torch.onnx.export(net, dummy, str(onnx_path), input_names=["input"], output_names=["output"],
                      dynamic_axes={"input": {2: "h", 3: "w"}, "output": {2: "h4", 3: "w4"}},
                      opset_version=17, dynamo=False)
    print(f"converted {pth.name} to ONNX", flush=True)
    return onnx_path


class Upscaler:
    """A model on the GPU (DirectML), else the CPU: RGB float images in [0, 1], 4x out."""

    def __init__(self, name: str):
        import onnxruntime as ort

        self.model = MODELS[name]
        path = ensure_onnx(self.model)
        options = ort.SessionOptions()
        options.log_severity_level = 3
        self.session = ort.InferenceSession(str(path), options,
                                            providers=["DmlExecutionProvider", "CPUExecutionProvider"])

    def __call__(self, rgb: np.ndarray) -> np.ndarray:
        """(h, w, 3) float32 in [0, 1] -> (4h, 4w, 3) float32 in [0, 1]."""
        x = np.ascontiguousarray(rgb.transpose(2, 0, 1)[None], dtype=np.float32)
        y = self.session.run(None, {"input": x})[0][0]
        return np.clip(y.transpose(1, 2, 0), 0.0, 1.0)
