from io import BytesIO

import pytest
from fastapi.testclient import TestClient
from PIL import Image

from app.main import app


@pytest.fixture
def client() -> TestClient:
    return TestClient(app)


def png_bytes(width: int = 32, height: int = 32) -> bytes:
    buffer = BytesIO()
    Image.new("RGB", (width, height), (255, 255, 255)).save(buffer, format="PNG")
    return buffer.getvalue()
