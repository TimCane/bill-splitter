FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
     && apt-get -y install --no-install-recommends \
        tmux \
     && rm -rf /var/lib/apt/lists/*

RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && nvm install --lts && nvm use --lts && npm install -g typescript" 2>&1