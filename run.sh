#!/bin/bash

# ServiceHub - Full Stack Development Server Runner
# Starts both the ServiceHub API (.NET) and UI (React) in parallel

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$SCRIPT_DIR/services/api"
WEB_DIR="$SCRIPT_DIR/apps/web"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Function to cleanup background processes on exit
cleanup() {
    echo ""
    echo -e "${YELLOW}Shutting down services...${NC}"
    kill $API_PID 2>/dev/null || true
    kill $WEB_PID 2>/dev/null || true
    wait $API_PID 2>/dev/null || true
    wait $WEB_PID 2>/dev/null || true
    echo -e "${GREEN}✓ All services stopped${NC}"
    exit 0
}

# Trap SIGINT (Ctrl+C) to cleanup gracefully
trap cleanup SIGINT SIGTERM

# Print header
echo -e "${BLUE}╔════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║   ServiceHub - Full Stack Development   ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════╝${NC}"
echo ""

# Check if required directories exist
if [ ! -d "$API_DIR" ]; then
    echo -e "${RED}✗ Error: API directory not found at $API_DIR${NC}"
    exit 1
fi

if [ ! -d "$WEB_DIR" ]; then
    echo -e "${RED}✗ Error: Web directory not found at $WEB_DIR${NC}"
    exit 1
fi

# Check prerequisites
echo -e "${YELLOW}Checking prerequisites...${NC}"

if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}✗ Error: dotnet CLI is not installed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ dotnet installed${NC}"

if ! command -v node &> /dev/null; then
    echo -e "${RED}✗ Error: Node.js is not installed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Node.js installed${NC}"

if ! command -v npm &> /dev/null; then
    echo -e "${RED}✗ Error: npm is not installed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ npm installed${NC}"

echo ""
echo -e "${YELLOW}Starting services...${NC}"
echo ""

# Start API in background
echo -e "${BLUE}Starting API...${NC}"
cd "$API_DIR"
bash run-api.sh &
API_PID=$!
echo -e "${GREEN}✓ API started (PID: $API_PID)${NC}"

# Give API a moment to start
sleep 3

# Install web dependencies if needed
if [ ! -d "$WEB_DIR/node_modules" ]; then
    echo -e "${BLUE}Installing web dependencies...${NC}"
    cd "$WEB_DIR"
    npm install
fi

# Start Web UI in background
echo -e "${BLUE}Starting UI...${NC}"
cd "$WEB_DIR"
npm run dev &
WEB_PID=$!
echo -e "${GREEN}✓ UI started (PID: $WEB_PID)${NC}"

echo ""
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo -e "${GREEN}✓ Both services are running!${NC}"
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo ""
echo -e "${BLUE}API:${NC}"
echo -e "  • ${GREEN}HTTP: http://localhost:5000${NC}"
echo -e "  • ${GREEN}HTTPS: https://localhost:5001${NC}"
echo -e "  • ${GREEN}Swagger: http://localhost:5000/swagger${NC}"
echo ""
echo -e "${BLUE}UI:${NC}"
echo -e "  • ${GREEN}http://localhost:5173${NC}"
echo ""
echo -e "${YELLOW}Press Ctrl+C to stop all services${NC}"
echo ""

# Wait for both processes
wait
