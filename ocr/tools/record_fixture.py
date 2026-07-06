"""Dev helper: record a real receipt into the parser's fixture corpus.

Not wired into the app runtime. POSTs a receipt image at a running OCR sidecar
and writes the response to ``ocr.json`` in the corpus layout, alongside a copy of
the image - the recorded half of a fixture. Hand-author ``expected.json`` next,
then run ReceiptParserCorpusTests (docs/11-testing-strategy.md#receiptparser).

Usage:
    python -m tools.record_fixture <image> <fixture-name> [--url URL] [--corpus DIR]

The default URL targets the sidecar over the compose network; forward the port
(or pass --url http://localhost:8000/ocr) when running from the host. Example:

    python -m tools.record_fixture ~/receipts/thai.jpg thai-set-menu
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import urllib.request
from pathlib import Path

_DEFAULT_URL = os.getenv("OCR_URL", "http://ocr:8000/ocr")

# ocr/ -> repo root -> the C# fixture corpus.
_DEFAULT_CORPUS = (
    Path(__file__).resolve().parents[2]
    / "backend"
    / "tests"
    / "BillSplitter.Tests"
    / "Fixtures"
    / "receipts"
)

_CONTENT_TYPES = {".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png"}


def _content_type(image: Path) -> str:
    content_type = _CONTENT_TYPES.get(image.suffix.lower())
    if content_type is None:
        raise SystemExit(f"unsupported image type {image.suffix!r} (jpeg or png only)")
    return content_type


def record(image: Path, name: str, url: str, corpus: Path) -> Path:
    body = image.read_bytes()
    request = urllib.request.Request(
        url, data=body, headers={"Content-Type": _content_type(image)}, method="POST"
    )
    with urllib.request.urlopen(request) as response:
        result = json.load(response)

    fixture = corpus / name
    fixture.mkdir(parents=True, exist_ok=True)
    (fixture / "ocr.json").write_text(json.dumps(result, indent=2) + "\n")
    shutil.copyfile(image, fixture / f"receipt{image.suffix.lower()}")
    return fixture


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("image", type=Path, help="the receipt image (jpeg or png)")
    parser.add_argument("name", help="fixture directory name, e.g. thai-set-menu")
    parser.add_argument("--url", default=_DEFAULT_URL, help=f"sidecar /ocr URL [{_DEFAULT_URL}]")
    parser.add_argument("--corpus", type=Path, default=_DEFAULT_CORPUS, help="corpus root")
    args = parser.parse_args(argv)

    if not args.image.is_file():
        raise SystemExit(f"no such image: {args.image}")

    fixture = record(args.image, args.name, args.url, args.corpus)
    print(f"recorded {fixture / 'ocr.json'}", file=sys.stderr)
    print(f"now hand-author {fixture / 'expected.json'} and run ReceiptParserCorpusTests")


if __name__ == "__main__":
    main()
