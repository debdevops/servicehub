#!/bin/bash

# ServiceHub - Full Stack Development Server Runner
# Starts both the ServiceHub API (.NET) and UI (React) in parallel

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$SCRIPT_DIR/services/api"
WEB_DIR="$SCRIPT_DIR/apps/web"
API_HTTP_URL="http://localhost:5153"
API_HTTPS_URL="https://localhost:7252"
WEB_PORT=3000

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

# PHASE 1: AGGRESSIVE CLEANUP
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Cleaning Previous Sessions        ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# Kill all existing processes
echo -e "${YELLOW}Killing previous processes...${NC}"
pkill -f "dotnet.*ServiceHub" 2>/dev/null || true
pkill -f "npm.*dev.*3000" 2>/dev/null || true
pkill -f "npm.*dev.*5173" 2>/dev/null || true
pkill -f "vite" 2>/dev/null || true
sleep 1

# Force kill any stubborn processes on the ports
echo -e "${YELLOW}Force-closing ports 5153 and 3000...${NC}"
lsof -ti:5153 | xargs kill -9 2>/dev/null || true
lsof -ti:3000 | xargs kill -9 2>/dev/null || true
lsof -ti:5173 | xargs kill -9 2>/dev/null || true
sleep 2

# Clean temporary files and logs
echo -e "${YELLOW}Cleaning temporary files...${NC}"
rm -f /tmp/servicehub_api.log 2>/dev/null || true
rm -f /tmp/servicehub_ui.log 2>/dev/null || true
rm -f /tmp/servicehub_*.log 2>/dev/null || true

# Clean API build artifacts if needed
echo -e "${YELLOW}Cleaning API build artifacts...${NC}"
cd "$API_DIR"
find . -type d -name "bin" -o -name "obj" | head -5 | while read dir; do
  [ -d "$dir" ] && echo "  Cleaning $dir" && rm -rf "$dir" 2>/dev/null || true
done

# Clean npm cache for web
echo -e "${YELLOW}Cleaning npm cache...${NC}"
cd "$WEB_DIR"
rm -rf node_modules/.vite 2>/dev/null || true
rm -f package-lock.json 2>/dev/null || true

echo ""
echo -e "${GREEN}✓ Cleanup complete${NC}"
echo ""

# Print header
echo -e "${BLUE}╔════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║   ServiceHub - Full Stack Development   ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════╝${NC}"
echo ""

# PHASE 2: VERIFY PREREQUISITES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Verifying Prerequisites           ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# Check if required directories exist
if [ ! -d "$API_DIR" ]; then
    echo -e "${RED}✗ Error: API directory not found at $API_DIR${NC}"
    exit 1
fi
echo -e "${GREEN}✓ API directory exists${NC}"

if [ ! -d "$WEB_DIR" ]; then
    echo -e "${RED}✗ Error: Web directory not found at $WEB_DIR${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Web directory exists${NC}"

# Check prerequisites
echo ""
echo -e "${YELLOW}Checking system tools...${NC}"

if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}✗ Error: dotnet CLI is not installed${NC}"
    exit 1
fi
DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
echo -e "${GREEN}✓ dotnet installed (v$DOTNET_VERSION)${NC}"

if ! command -v node &> /dev/null; then
    echo -e "${RED}✗ Error: Node.js is not installed${NC}"
    exit 1
fi
NODE_VERSION=$(node --version 2>/dev/null || echo "unknown")
echo -e "${GREEN}✓ Node.js installed ($NODE_VERSION)${NC}"

if ! command -v npm &> /dev/null; then
    echo -e "${RED}✗ Error: npm is not installed${NC}"
    exit 1
fi
NPM_VERSION=$(npm --version 2>/dev/null || echo "unknown")
echo -e "${GREEN}✓ npm installed (v$NPM_VERSION)${NC}"

if ! command -v lsof &> /dev/null; then
    echo -e "${RED}✗ Error: lsof is not installed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ lsof installed${NC}"

echo ""
echo -e "${GREEN}✓ All prerequisites verified${NC}"
echo ""

# PHASE 3: PORT VERIFICATION
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Verifying Ports Available         ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

for PORT in 5153 $WEB_PORT; do
    PID=$(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null || true)
    if [ -n "$PID" ]; then
        echo -e "${YELLOW}⚠ Port $PORT in use (PID: $PID). Force-stopping...${NC}"
        kill -9 $PID 2>/dev/null || true
        sleep 1
    else
        echo -e "${GREEN}✓ Port $PORT available${NC}"
    fi
done

echo ""

# PHASE 4: START SERVICES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║        Starting Services              ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# Start API in background
echo -e "${BLUE}Starting API...${NC}"
cd "$API_DIR"
bash run-api.sh > /tmp/servicehub_api_startup.log 2>&1 &
API_PID=$!
echo -e "${GREEN}✓ API process started (PID: $API_PID)${NC}"

# Wait for API to be ready (max 15 seconds)
echo -e "${YELLOW}Waiting for API to be ready...${NC}"
WAIT_COUNT=0
MAX_WAIT=15
API_READY=false
while [ $WAIT_COUNT -lt $MAX_WAIT ]; do
    if curl -s http://localhost:5153/health >/dev/null 2>&1; then
        API_READY=true
        break
    fi
    sleep 1
    WAIT_COUNT=$((WAIT_COUNT + 1))
done

if [ "$API_READY" = true ]; then
    echo -e "${GREEN}✓ API is ready${NC}"
else
    echo -e "${YELLOW}⚠ API startup check timed out (continuing anyway)${NC}"
fi

echo ""

# Install web dependencies if needed
if [ ! -d "$WEB_DIR/node_modules" ]; then
    echo -e "${BLUE}Installing web dependencies (first time)...${NC}"
    cd "$WEB_DIR"
    npm install
fi

# Start Web UI in background
echo -e "${BLUE}Starting UI...${NC}"
cd "$WEB_DIR"
npm run dev -- --port $WEB_PORT --strictPort > /tmp/servicehub_ui_startup.log 2>&1 &
WEB_PID=$!
echo -e "${GREEN}✓ UI process started (PID: $WEB_PID)${NC}"

# Wait for UI to be ready (max 10 seconds)
echo -e "${YELLOW}Waiting for UI to be ready...${NC}"
WAIT_COUNT=0
MAX_WAIT=10
UI_READY=false
while [ $WAIT_COUNT -lt $MAX_WAIT ]; do
    if curl -s http://localhost:$WEB_PORT >/dev/null 2>&1; then
        UI_READY=true
        break
    fi
    sleep 1
    WAIT_COUNT=$((WAIT_COUNT + 1))
done

if [ "$UI_READY" = true ]; then
    echo -e "${GREEN}✓ UI is ready${NC}"
else
    echo -e "${YELLOW}⚠ UI startup check timed out (continuing anyway)${NC}"
fi

echo ""

# PHASE 5: SERVICES READY
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║   ✓ All Services Running Successfully!  ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""
echo -e "${BLUE}📍 API Endpoints:${NC}"
echo -e "  • ${GREEN}HTTP:  ${API_HTTP_URL}${NC}"
echo -e "  • ${GREEN}HTTPS: ${API_HTTPS_URL}${NC}"
echo -e "  • ${GREEN}Swagger: ${API_HTTP_URL}/swagger${NC}"
echo ""
echo -e "${BLUE}🌐 Web UI:${NC}"
echo -e "  • ${GREEN}http://localhost:${WEB_PORT}${NC}"
echo ""
echo -e "${BLUE}📋 Process IDs:${NC}"
echo -e "  • ${GREEN}API:  $API_PID${NC}"
echo -e "  • ${GREEN}UI:   $WEB_PID${NC}"
echo ""
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo -e "${BLUE}Press ${YELLOW}Ctrl+C${BLUE} to stop all services${NC}"
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo ""

# Keep services running
wait
