"""FastAPI sidecar: image in, text lines with geometry out. No state, no Redis,
no MinIO (docs/06-ocr-service.md)."""

from __future__ import annotations

from io import BytesIO

from fastapi import FastAPI, HTTPException, Request
from PIL import Image, UnidentifiedImageError

from .config import get_settings
from .models import OcrResponse
from .recognizer import load_stub_response

app = FastAPI(title="bill-splitter ocr", version="1.0.0")


@app.get("/healthz")
def healthz() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/ocr", response_model=OcrResponse)
async def ocr(request: Request) -> OcrResponse:
    settings = get_settings()

    body = await _read_bounded(request, settings.max_bytes)
    if body is None:
        raise HTTPException(status_code=413, detail="image exceeds the size limit")

    _validate_image(body, settings.max_dimension)

    if settings.stub:
        return load_stub_response()

    # Real inference lands in M3; a build without stub mode has nothing to run.
    raise HTTPException(status_code=501, detail="real OCR inference is not enabled")


async def _read_bounded(request: Request, max_bytes: int) -> bytes | None:
    """Read the body but never buffer more than max_bytes. Returns None once the
    upload exceeds the limit so an oversized POST cannot exhaust memory. The
    Content-Length header (when honest) short-circuits before the first read;
    the streaming cap covers chunked or lying senders."""
    declared = request.headers.get("content-length")
    if declared is not None and declared.isdigit() and int(declared) > max_bytes:
        return None

    chunks: list[bytes] = []
    total = 0
    async for chunk in request.stream():
        total += len(chunk)
        if total > max_bytes:
            return None
        chunks.append(chunk)
    return b"".join(chunks)


def _validate_image(body: bytes, max_dimension: int) -> None:
    """Reject undecodable images and oversized dimensions before any inference.
    Pillow reads the header lazily, so this does not fully decode the pixels."""
    try:
        with Image.open(BytesIO(body)) as image:
            width, height = image.size
    except (UnidentifiedImageError, OSError) as error:
        raise HTTPException(status_code=422, detail="undecodable image") from error

    if width > max_dimension or height > max_dimension:
        raise HTTPException(
            status_code=422,
            detail=f"image dimensions exceed {max_dimension}px",
        )
