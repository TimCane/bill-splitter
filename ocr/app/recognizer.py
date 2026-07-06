"""Turns image bytes into OCR lines. Two paths share one response shape: the
stub echoes a fixture (fast, dependency-free - used by tests and local dev), and
the real path preprocesses the image (docs/06-ocr-service.md#preprocessing) then
runs PaddleOCR PP-OCRv4 English det+rec on CPU."""

from __future__ import annotations

import json
import time
from functools import lru_cache
from pathlib import Path
from typing import Any

from PIL import Image, ImageFilter, ImageOps

from .config import Settings, get_settings
from .models import Box, OcrLine, OcrResponse

_FIXTURE = Path(__file__).resolve().parent.parent / "fixtures" / "sample-receipt.json"


@lru_cache(maxsize=1)
def load_stub_response() -> OcrResponse:
    data = json.loads(_FIXTURE.read_text())
    return OcrResponse.model_validate(data)


@lru_cache(maxsize=2)
def _engine(use_angle_cls: bool) -> Any:
    """The PaddleOCR engine, built once per angle-classifier setting and cached.
    English det+rec; the angle classifier (config-gated) rights sideways and
    upside-down crops. Models are baked into the image at build time so this
    never reaches the network."""
    # Imported lazily: the stub path (and every test) must run without the
    # ~600MB PaddlePaddle wheel installed.
    from paddleocr import PaddleOCR

    return PaddleOCR(use_angle_cls=use_angle_cls, lang="en", show_log=False)


def warmup() -> None:
    """Build the engine ahead of the first request so cold start is paid at
    boot, not on a user's upload. Called from the app's startup path when stub
    mode is off (docs/06-ocr-service.md#stack)."""
    _engine(get_settings().angle_cls)


def _upscale(image: Image.Image, min_side: int) -> Image.Image:
    """Enlarge so the shorter side reaches ``min_side``. Phone crops of a thermal
    receipt are often too small for det+rec; upscaling recovers thin strokes.
    Never downscales - a large, sharp photo is left as-is."""
    width, height = image.size
    shortest = min(width, height)
    if shortest == 0 or shortest >= min_side:
        return image
    scale = min_side / shortest
    return image.resize((round(width * scale), round(height * scale)), Image.LANCZOS)


def _otsu_threshold(histogram: list[int]) -> int:
    """Otsu's method: the 0-255 cut that maximises between-class variance."""
    total = sum(histogram)
    sum_all = sum(value * count for value, count in enumerate(histogram))
    weight_bg = 0
    sum_bg = 0.0
    best_variance = -1.0
    threshold = 127
    for value, count in enumerate(histogram):
        weight_bg += count
        if weight_bg == 0:
            continue
        weight_fg = total - weight_bg
        if weight_fg == 0:
            break
        sum_bg += value * count
        mean_bg = sum_bg / weight_bg
        mean_fg = (sum_all - sum_bg) / weight_fg
        variance = weight_bg * weight_fg * (mean_bg - mean_fg) ** 2
        if variance > best_variance:
            best_variance = variance
            threshold = value
    return threshold


def _binarize(gray: Image.Image) -> Image.Image:
    """Otsu black-and-white. Off by default: it helps only clean, high-contrast
    scans and can erase faint thermal print, so it is an opt-in flag."""
    threshold = _otsu_threshold(gray.histogram())
    return gray.point(lambda pixel: 255 if pixel > threshold else 0)


def preprocess_image(image: Image.Image, settings: Settings) -> Image.Image:
    """Grayscale, denoise, normalise contrast and upscale before inference. Pure
    Pillow so it runs (and is unit-tested) without the numpy/paddle wheels. Skew
    and rotation are left to PaddleOCR's angle classifier, not fixed here."""
    gray = ImageOps.grayscale(image)
    gray = gray.filter(ImageFilter.MedianFilter(size=3))  # drop speckle noise
    gray = ImageOps.autocontrast(gray, cutoff=1)  # spread the histogram
    gray = _upscale(gray, settings.upscale_min_side)
    if settings.binarize:
        gray = _binarize(gray)
    return gray


def run_inference(body: bytes) -> OcrResponse:
    """Decode the image and run detection+recognition, returning lines ordered
    top-to-bottom. Raises on decode or inference failure; the caller maps that
    to the HTTP error contract."""
    from io import BytesIO

    import numpy as np

    settings = get_settings()
    with Image.open(BytesIO(body)) as image:
        prepared = preprocess_image(image, settings) if settings.preprocess else image
        # PaddleOCR expects a cv2-style BGR array; PIL decodes RGB.
        rgb = np.asarray(prepared.convert("RGB"))
    bgr = rgb[:, :, ::-1]

    start = time.monotonic()
    raw = _engine(settings.angle_cls).ocr(bgr, cls=settings.angle_cls)
    duration_ms = int((time.monotonic() - start) * 1000)

    return _response_from_raw(raw, duration_ms)


def _response_from_raw(raw: Any, duration_ms: int) -> OcrResponse:
    """Shape PaddleOCR's nested output into the response contract. PaddleOCR
    returns one entry per submitted image (we submit one), each a list of
    ``[quad, (text, confidence)]`` detections, or ``None`` when nothing is
    found."""
    detections = raw[0] if raw else None
    lines = [_line_from_detection(det) for det in (detections or [])]
    lines.sort(key=lambda line: line.box.y)
    return OcrResponse(durationMs=duration_ms, lines=lines)


def _line_from_detection(detection: Any) -> OcrLine:
    quad, (text, confidence) = detection
    return OcrLine(text=text, confidence=float(confidence), box=_axis_aligned_box(quad))


def _axis_aligned_box(quad: Any) -> Box:
    """The bounding rectangle of PaddleOCR's (possibly skewed) quad, in pixels
    of the submitted image."""
    xs = [float(point[0]) for point in quad]
    ys = [float(point[1]) for point in quad]
    left, top = min(xs), min(ys)
    return Box(
        x=int(round(left)),
        y=int(round(top)),
        width=int(round(max(xs) - left)),
        height=int(round(max(ys) - top)),
    )
