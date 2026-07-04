#!/bin/bash
# Post-create setup for the bill-spliiter dev container

# Shell aliases
cat >> ~/.aliases << 'EOF'
alias rc='redis-cli -h redis -p 6379'
alias cc='tmux new -A -s claude "claude --dangerously-skip-permissions"'
EOF

# Source aliases and print them on terminal open
cat >> ~/.bashrc << 'BASHRC'
source ~/.aliases

echo ""
echo "  bill-spliiter shortcuts:"
echo "  ─────────────────────────────────────────"
echo "  rc          Redis CLI"
echo "  pg          PostgreSQL CLI"
echo "  pgd         PostgreSQL CLI connected to bill-spliiter database"
echo "  cc          Claude session"
echo "  ─────────────────────────────────────────"
echo ""
BASHRC

# tmux config
cp /workspaces/bill-spliiter/.devcontainer/.tmux.conf ~/.tmux.conf

# Frontend dependencies + Puppeteer's bundled Chrome
cd /workspaces/bill-spliiter/frontend && npm install && npx puppeteer browsers install chrome
