import pytest
from fastapi.testclient import TestClient

from tests.conftest import png_bytes


@pytest.fixture(autouse=True)
def stub_mode(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OCR_STUB", "true")


def test_ocr_echoes_fixture(client: TestClient) -> None:
    response = client.post(
        "/ocr",
        content=png_bytes(),
        headers={"Content-Type": "image/png"},
    )

    assert response.status_code == 200
    body = response.json()
    assert isinstance(body["durationMs"], int)
    assert body["lines"]
    first = body["lines"][0]
    assert set(first) == {"text", "confidence", "box"}
    assert set(first["box"]) == {"x", "y", "width", "height"}


def test_ocr_rejects_undecodable(client: TestClient) -> None:
    response = client.post(
        "/ocr",
        content=b"not an image",
        headers={"Content-Type": "image/png"},
    )

    assert response.status_code == 422


def test_ocr_rejects_oversized(client: TestClient, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OCR_MAX_BYTES", "16")

    response = client.post(
        "/ocr",
        content=png_bytes(),
        headers={"Content-Type": "image/png"},
    )

    assert response.status_code == 413


def test_ocr_rejects_oversized_dimensions(
    client: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("OCR_MAX_DIMENSION", "16")

    response = client.post(
        "/ocr",
        content=png_bytes(32, 32),
        headers={"Content-Type": "image/png"},
    )

    assert response.status_code == 422
