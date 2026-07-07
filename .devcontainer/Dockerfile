FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

# tmux for the claude/dev panes; python3-venv/pip to build the OCR virtualenv in
# post-create; libgomp1 + libgl1 + libglib2.0-0 are the native libs PaddlePaddle
# and paddleocr's opencv need at runtime (mirrors ocr/Dockerfile).
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
     && apt-get -y install --no-install-recommends \
        tmux \
        python3-venv \
        python3-pip \
        libgomp1 \
        libgl1 \
        libglib2.0-0 \
     && rm -rf /var/lib/apt/lists/*

RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && nvm install --lts && nvm use --lts && npm install -g typescript pnpm" 2>&1