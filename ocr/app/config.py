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
    # wiring lands in M3; until then the sidecar always runs stubbed.
    stub: bool
    max_bytes: int
    max_dimension: int


def get_settings() -> Settings:
    return Settings(
        stub=os.getenv("OCR_STUB", "true").lower() in _TRUTHY,
        max_bytes=int(os.getenv("OCR_MAX_BYTES", str(_DEFAULT_MAX_BYTES))),
        max_dimension=int(os.getenv("OCR_MAX_DIMENSION", str(_DEFAULT_MAX_DIMENSION))),
    )
