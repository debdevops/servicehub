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
echo -e "  • ${GREEN}http://localhost:5000${NC}"
echo -e "  • ${GREEN}https://localhost:5001${NC}"
echo ""
echo -e "${YELLOW}Swagger UI: ${GREEN}http://localhost:5000/swagger${NC}"
echo ""
echo -e "${YELLOW}Ctrl+C to stop the server${NC}"
echo ""

# Run the API
cd "$SCRIPT_DIR"
dotnet run --project "$PROJECT_FILE"
