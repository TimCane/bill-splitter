FROM python:3.12-slim

WORKDIR /app

ENV PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    OCR_STUB=false

# Native libs the slim base lacks: paddle needs OpenMP (libgomp), and
# paddleocr's opencv-python needs libGL + glib.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt requirements-ocr.txt ./
RUN pip install --no-cache-dir -r requirements.txt -r requirements-ocr.txt

COPY app ./app
COPY fixtures ./fixtures

# Bake the PP-OCRv4 English det+rec models into the image so cold start never
# reaches the network (docs/06-ocr-service.md#stack).
RUN python -c "from app.recognizer import warmup; warmup()"

EXPOSE 8000

# One worker: concurrency is throttled by the backend queue, not here.
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=15s \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/healthz').read()"

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "1"]
