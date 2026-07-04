#!/bin/bash
# Post-create setup for the bill-splitter dev container

# Shell aliases
cat >> ~/.aliases << 'EOF'
alias rc='redis-cli -h redis -p 6379'
alias cc='tmux new -A -s claude "claude --dangerously-skip-permissions"'
EOF

# Source aliases and print them on terminal open
cat >> ~/.bashrc << 'BASHRC'
source ~/.aliases

echo ""
echo "  bill-splitter shortcuts:"
echo "  ─────────────────────────────────────────"
echo "  rc          Redis CLI"
echo "  cc          Claude session"
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
