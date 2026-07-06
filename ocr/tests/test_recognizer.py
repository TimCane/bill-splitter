"""The shaping helpers are pure and paddle-free, so they run in CI without the
inference wheels. Real inference is exercised by the e2e smoke, not here
(docs/11-testing-strategy.md)."""

from app.recognizer import _axis_aligned_box, _response_from_raw


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
