#!/bin/bash

# ServiceHub API - Development Server Runner
# Starts the ServiceHub.Api in development mode with hot reload

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$SCRIPT_DIR/src/ServiceHub.Api/ServiceHub.Api.csproj"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${BLUE}╔════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║     ServiceHub API - Development       ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════╝${NC}"
echo ""

# Check if project file exists
if [ ! -f "$PROJECT_FILE" ]; then
    echo -e "${YELLOW}❌ Error: Project file not found at $PROJECT_FILE${NC}"
    exit 1
fi

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo -e "${YELLOW}❌ Error: dotnet CLI is not installed${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Starting ServiceHub API...${NC}"
echo -e "${BLUE}Project: ServiceHub.Api${NC}"
echo -e "${BLUE}Location: $PROJECT_FILE${NC}"
echo -e "${BLUE}Mode: Development${NC}"
echo ""
echo -e "${YELLOW}API will be available at:${NC}"
echo -e "  • ${GREEN}http://localhost:5153${NC} (local)"
echo -e "  • ${GREEN}http://0.0.0.0:5153${NC} (all interfaces)"
echo ""
echo -e "${YELLOW}Swagger UI: ${GREEN}http://localhost:5153/swagger${NC}"
echo ""
echo -e "${YELLOW}Ctrl+C to stop the server${NC}"
echo ""

# Run the API
# --urls binds Kestrel to all network interfaces so remote machines can reach the API.
# When running locally, http://localhost:5153 still works.
# When running on a server, http://serverip:5153 is also reachable.
cd "$SCRIPT_DIR"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
echo -e "${BLUE}Environment: $ASPNETCORE_ENVIRONMENT${NC}"
dotnet run --project "$PROJECT_FILE" \
  --urls "http://0.0.0.0:5153"
