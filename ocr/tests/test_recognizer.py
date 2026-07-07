"""The shaping and preprocessing helpers are pure Pillow and paddle-free, so
they run in CI without the numpy/inference wheels. Real inference is exercised by
the e2e smoke, not here (docs/11-testing-strategy.md)."""

import dataclasses

from PIL import Image

from app.config import Settings, get_settings
from app.recognizer import (
    _axis_aligned_box,
    _otsu_threshold,
    _response_from_raw,
    _upscale,
    preprocess_image,
)


def _settings(**overrides: object) -> Settings:
    return dataclasses.replace(get_settings(), **overrides)


def test_axis_aligned_box_wraps_a_skewed_quad() -> None:
    quad = [[42, 310], [870, 306], [872, 344], [40, 348]]
    box = _axis_aligned_box(quad)

    assert (box.x, box.y) == (40, 306)
    assert box.width == 832
    assert box.height == 42


def test_response_orders_lines_top_to_bottom() -> None:
    raw = [
        [
            [[[0, 200], [10, 200], [10, 220], [0, 220]], ("TOTAL 9.00", 0.96)],
            [[[0, 40], [10, 40], [10, 60], [0, 60]], ("COFFEE 3.00", 0.94)],
        ]
    ]

    response = _response_from_raw(raw, duration_ms=1234)

    assert response.durationMs == 1234
    assert [line.text for line in response.lines] == ["COFFEE 3.00", "TOTAL 9.00"]


def test_response_handles_no_detections() -> None:
    assert _response_from_raw([None], duration_ms=5).lines == []
    assert _response_from_raw(None, duration_ms=5).lines == []


def test_upscale_enlarges_small_images_to_min_side() -> None:
    upscaled = _upscale(Image.new("L", (100, 60), 255), min_side=300)

    assert upscaled.size == (500, 300)


def test_upscale_leaves_large_images_untouched() -> None:
    image = Image.new("L", (1200, 2000), 255)

    assert _upscale(image, min_side=1000) is image


def test_preprocess_returns_grayscale() -> None:
    image = Image.new("RGB", (1200, 1600), (200, 180, 160))

    assert preprocess_image(image, _settings(binarize=False)).mode == "L"


def test_preprocess_binarize_yields_two_tones() -> None:
    image = Image.new("RGB", (1200, 1600), (128, 128, 128))
    image.paste((10, 10, 10), (0, 0, 600, 1600))  # a dark half so Otsu has two classes

    out = preprocess_image(image, _settings(binarize=True))

    assert set(out.getdata()) <= {0, 255}


def test_otsu_threshold_splits_a_bimodal_histogram() -> None:
    histogram = [0] * 256
    histogram[30] = histogram[220] = 500

    assert 30 <= _otsu_threshold(histogram) < 220
