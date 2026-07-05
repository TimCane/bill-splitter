"""Turns image bytes into OCR lines. M1 ships only the stub path (echo a
fixture); real PaddleOCR inference lands in M3 (docs/14-build-order.md)."""

from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

from .models import OcrResponse

_FIXTURE = Path(__file__).resolve().parent.parent / "fixtures" / "sample-receipt.json"


@lru_cache(maxsize=1)
def load_stub_response() -> OcrResponse:
    data = json.loads(_FIXTURE.read_text())
    return OcrResponse.model_validate(data)
