"""Response schema for POST /ocr - the sidecar's half of the contract in
docs/06-ocr-service.md."""

from __future__ import annotations

from pydantic import BaseModel


class Box(BaseModel):
    """Axis-aligned bounding rectangle in pixels of the submitted image."""

    x: int
    y: int
    width: int
    height: int


class OcrLine(BaseModel):
    text: str
    confidence: float
    box: Box


class OcrResponse(BaseModel):
    durationMs: int
    lines: list[OcrLine]
