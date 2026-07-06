"""Runtime configuration, read from the environment on each access so tests can
flip flags without reimporting the app."""

from __future__ import annotations

import os
from dataclasses import dataclass

_TRUTHY = {"1", "true", "yes", "on"}

_DEFAULT_MAX_BYTES = 10 * 1024 * 1024
_DEFAULT_MAX_DIMENSION = 8000
_DEFAULT_UPSCALE_MIN_SIDE = 1000


def _flag(name: str, default: bool) -> bool:
    return os.getenv(name, str(default)).lower() in _TRUTHY


@dataclass(frozen=True)
class Settings:
    # Stub mode echoes a fixture instead of running inference. Real PaddleOCR
    # wiring lands in M3. Defaults off so a deploy that forgets to set OCR_STUB
    # fails loud (501) rather than serving the fixture as if it were real OCR;
    # the M1 sidecar image opts in explicitly (OCR_STUB=true in the Dockerfile).
    stub: bool
    max_bytes: int
    max_dimension: int
    # Preprocessing (docs/06-ocr-service.md#preprocessing). preprocess runs the
    # grayscale/denoise/contrast/upscale pipeline; angle_cls lets PaddleOCR right
    # rotated crops; binarize is opt-in (helps clean scans, hurts faint print).
    preprocess: bool
    angle_cls: bool
    binarize: bool
    upscale_min_side: int


def get_settings() -> Settings:
    return Settings(
        stub=_flag("OCR_STUB", False),
        max_bytes=int(os.getenv("OCR_MAX_BYTES", str(_DEFAULT_MAX_BYTES))),
        max_dimension=int(os.getenv("OCR_MAX_DIMENSION", str(_DEFAULT_MAX_DIMENSION))),
        preprocess=_flag("OCR_PREPROCESS", True),
        angle_cls=_flag("OCR_ANGLE_CLS", True),
        binarize=_flag("OCR_BINARIZE", False),
        upscale_min_side=int(os.getenv("OCR_UPSCALE_MIN_SIDE", str(_DEFAULT_UPSCALE_MIN_SIDE))),
    )
