"""Turns image bytes into OCR lines. Two paths share one response shape: the
stub echoes a fixture (fast, dependency-free - used by tests and local dev), and
the real path runs PaddleOCR PP-OCRv4 English det+rec on CPU
(docs/06-ocr-service.md)."""

from __future__ import annotations

import json
import time
from functools import lru_cache
from pathlib import Path
from typing import Any

from .models import Box, OcrLine, OcrResponse

_FIXTURE = Path(__file__).resolve().parent.parent / "fixtures" / "sample-receipt.json"


@lru_cache(maxsize=1)
def load_stub_response() -> OcrResponse:
    data = json.loads(_FIXTURE.read_text())
    return OcrResponse.model_validate(data)


@lru_cache(maxsize=1)
def _engine() -> Any:
    """The PaddleOCR engine, built once and cached. English det+rec only, no
    angle classifier (receipts are photographed upright); models are baked into
    the image at build time so this never reaches the network."""
    # Imported lazily: the stub path (and every test) must run without the
    # ~600MB PaddlePaddle wheel installed.
    from paddleocr import PaddleOCR

    return PaddleOCR(use_angle_cls=False, lang="en", show_log=False)


def warmup() -> None:
    """Build the engine ahead of the first request so cold start is paid at
    boot, not on a user's upload. Called from the app's startup path when stub
    mode is off (docs/06-ocr-service.md#stack)."""
    _engine()


def run_inference(body: bytes) -> OcrResponse:
    """Decode the image and run detection+recognition, returning lines ordered
    top-to-bottom. Raises on decode or inference failure; the caller maps that
    to the HTTP error contract."""
    from io import BytesIO

    import numpy as np
    from PIL import Image

    with Image.open(BytesIO(body)) as image:
        # PaddleOCR expects a cv2-style BGR array; PIL decodes RGB.
        rgb = np.asarray(image.convert("RGB"))
    bgr = rgb[:, :, ::-1]

    start = time.monotonic()
    raw = _engine().ocr(bgr, cls=False)
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
