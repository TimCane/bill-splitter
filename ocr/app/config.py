"""Runtime configuration, read from the environment on each access so tests can
flip flags without reimporting the app."""

from __future__ import annotations

import os
from dataclasses import dataclass

_TRUTHY = {"1", "true", "yes", "on"}

_DEFAULT_MAX_BYTES = 10 * 1024 * 1024
_DEFAULT_MAX_DIMENSION = 8000


@dataclass(frozen=True)
class Settings:
    # Stub mode echoes a fixture instead of running inference. Real PaddleOCR
    # wiring lands in M3. Defaults off so a deploy that forgets to set OCR_STUB
    # fails loud (501) rather than serving the fixture as if it were real OCR;
    # the M1 sidecar image opts in explicitly (OCR_STUB=true in the Dockerfile).
    stub: bool
    max_bytes: int
    max_dimension: int


def get_settings() -> Settings:
    return Settings(
        stub=os.getenv("OCR_STUB", "false").lower() in _TRUTHY,
        max_bytes=int(os.getenv("OCR_MAX_BYTES", str(_DEFAULT_MAX_BYTES))),
        max_dimension=int(os.getenv("OCR_MAX_DIMENSION", str(_DEFAULT_MAX_DIMENSION))),
    )
