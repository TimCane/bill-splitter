#!/bin/bash
# Post-create setup for the bill-splitter dev container

# Shell aliases
cat >> ~/.aliases << 'EOF'
alias rc='redis-cli -h redis -p 6379'
alias cc='tmux new -A -s claude "claude --dangerously-skip-permissions"'
alias ocr='(cd /workspaces/bill-splitter/ocr && .venv/bin/uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload)'
EOF

# Source aliases and print them on terminal open
cat >> ~/.bashrc << 'BASHRC'
source ~/.aliases

echo ""
echo "  bill-splitter shortcuts:"
echo "  ─────────────────────────────────────────"
echo "  rc          Redis CLI"
echo "  cc          Claude session"
echo "  ocr         OCR sidecar (uvicorn, :8000)"
echo "  ─────────────────────────────────────────"
echo ""
BASHRC

# tmux config
cp /workspaces/bill-splitter/.devcontainer/.tmux.conf ~/.tmux.conf

# Frontend dependencies + Puppeteer's bundled Chrome; no-op until the
# frontend is scaffolded.
if [ -f /workspaces/bill-splitter/frontend/package.json ]; then
  cd /workspaces/bill-splitter/frontend && pnpm install && pnpm exec puppeteer browsers install chrome
fi

# OCR virtualenv. The sidecar now runs in-container as a dev process rather than
# its own compose service, so build its venv here: light web deps + the ~600MB
# PaddleOCR inference wheels, then bake the PP-OCRv4 models so the first request
# never reaches the network (docs/06-ocr-service.md). Idempotent - reuses an
# existing .venv on rebuild.
OCR_DIR=/workspaces/bill-splitter/ocr
if [ -f "$OCR_DIR/requirements.txt" ]; then
  cd "$OCR_DIR"
  [ -d .venv ] || python3 -m venv .venv
  .venv/bin/pip install --upgrade pip
  .venv/bin/pip install -r requirements.txt -r requirements-ocr.txt -r requirements-dev.txt
  .venv/bin/python -c "from app.recognizer import warmup; warmup()"
fi
