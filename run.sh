#!/usr/bin/env bash

# ServiceHub - Full Stack Development Server Runner
# Automatically installs prerequisites and starts both the ServiceHub API (.NET) and UI (React)
# Supports: macOS, Ubuntu/Debian, RHEL/CentOS/Fedora, Arch Linux, openSUSE, WSL

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$SCRIPT_DIR/services/api"
WEB_DIR="$SCRIPT_DIR/apps/web"
API_HTTP_URL="http://localhost:5153"
API_HTTPS_URL="https://localhost:7252"
WEB_PORT=3000

# Version requirements
REQUIRED_DOTNET_VERSION="8.0"
REQUIRED_NODE_MAJOR_VERSION="18"

# Global flags
IS_WSL=false
HAS_SUDO=false

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Check if running under WSL
detect_wsl() {
    if grep -qEi "(Microsoft|WSL)" /proc/version 2>/dev/null || 
       grep -qEi "(Microsoft|WSL)" /proc/sys/kernel/osrelease 2>/dev/null; then
        IS_WSL=true
        echo -e "${CYAN}ℹ Detected Windows Subsystem for Linux (WSL)${NC}"
    fi
}

# Check sudo availability
check_sudo() {
    if command -v sudo >/dev/null 2>&1; then
        HAS_SUDO=true
    else
        echo -e "${YELLOW}⚠ Warning: sudo not available. Some installations may require manual intervention.${NC}"
        HAS_SUDO=false
    fi
}

# Get Linux distribution info (portable across distros)
get_linux_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        DISTRO_ID="$ID"
        DISTRO_VERSION="$VERSION_ID"
        DISTRO_NAME="$NAME"
    elif [ -f /etc/lsb-release ]; then
        . /etc/lsb-release
        DISTRO_ID="$(echo "$DISTRIB_ID" | tr '[:upper:]' '[:lower:]')"
        DISTRO_VERSION="$DISTRIB_RELEASE"
        DISTRO_NAME="$DISTRIB_DESCRIPTION"
    elif [ -f /etc/redhat-release ]; then
        DISTRO_NAME=$(cat /etc/redhat-release)
        DISTRO_ID="rhel"
        DISTRO_VERSION="$(rpm -q --queryformat '%{VERSION}' centos-release 2>/dev/null || echo '0')"
    else
        DISTRO_ID="unknown"
        DISTRO_VERSION="unknown"
        DISTRO_NAME="Unknown Linux"
    fi
}

# Detect OS
detect_os() {
    case "$(uname -s)" in
        Darwin*)
            OS="macos"
            PACKAGE_MANAGER="brew"
            ;;
        Linux*)
            OS="linux"
            detect_wsl
            get_linux_distro
            
            # Detect package manager
            if command -v apt-get >/dev/null 2>&1; then
                PACKAGE_MANAGER="apt"
            elif command -v dnf >/dev/null 2>&1; then
                PACKAGE_MANAGER="dnf"
            elif command -v yum >/dev/null 2>&1; then
                PACKAGE_MANAGER="yum"
            elif command -v pacman >/dev/null 2>&1; then
                PACKAGE_MANAGER="pacman"
            elif command -v zypper >/dev/null 2>&1; then
                PACKAGE_MANAGER="zypper"
            elif command -v apk >/dev/null 2>&1; then
                PACKAGE_MANAGER="apk"
            else
                echo -e "${RED}✗ Error: No supported package manager found${NC}"
                echo -e "${YELLOW}Supported: apt, dnf, yum, pacman, zypper, apk${NC}"
                exit 1
            fi
            ;;
        FreeBSD*|OpenBSD*|NetBSD*)
            echo -e "${RED}✗ Error: BSD systems are not fully supported yet${NC}"
            echo -e "${YELLOW}Please install .NET 8 SDK and Node.js 18+ manually${NC}"
            exit 1
            ;;
        CYGWIN*|MINGW*|MSYS*)
            echo -e "${RED}✗ Error: Please use WSL (Windows Subsystem for Linux) on Windows${NC}"
            echo -e "${YELLOW}Instructions: https://docs.microsoft.com/windows/wsl/install${NC}"
            exit 1
            ;;
        *)
            echo -e "${RED}✗ Error: Unsupported operating system: $(uname -s)${NC}"
            exit 1
            ;;
    esac
    
    check_sudo
}

# Install Homebrew on macOS if not present
install_homebrew() {
    if [ "$OS" = "macos" ] && ! command -v brew &> /dev/null; then
        echo -e "${YELLOW}Homebrew not found. Installing Homebrew...${NC}"
        /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
        
        # Add Homebrew to PATH for Apple Silicon Macs
        if [ -f "/opt/homebrew/bin/brew" ]; then
            eval "$(/opt/homebrew/bin/brew shellenv)"
        fi
        
        echo -e "${GREEN}✓ Homebrew installed successfully${NC}"
    fi
}

# Check and install .NET SDK
check_and_install_dotnet() {
    local dotnet_installed=false
    local dotnet_version=""
    
    if command -v dotnet >/dev/null 2>&1; then
        dotnet_version=$(dotnet --version 2>/dev/null | cut -d'.' -f1)
        if [ "$dotnet_version" = "8" ]; then
            dotnet_installed=true
        fi
    fi
    
    if [ "$dotnet_installed" = false ]; then
        echo -e "${YELLOW}Installing .NET 8 SDK...${NC}"
        
        if [ "$OS" = "macos" ]; then
            brew install --cask dotnet-sdk
        elif [ "$OS" = "linux" ]; then
            # Add Microsoft package repository based on distro
            if [ "$PACKAGE_MANAGER" = "apt" ]; then
                # Detect Ubuntu/Debian version
                if [ -f /etc/os-release ]; then
                    . /etc/os-release
                    VERSION_NUM="${VERSION_ID:-22.04}"
                else
                    VERSION_NUM="22.04"  # Default fallback
                fi
                
                if [ "$HAS_SUDO" = true ]; then
                    wget -q https://packages.microsoft.com/config/ubuntu/${VERSION_NUM}/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb 2>/dev/null || 
                    wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
                    sudo dpkg -i /tmp/packages-microsoft-prod.deb
                    rm -f /tmp/packages-microsoft-prod.deb
                    sudo apt-get update
                    sudo apt-get install -y dotnet-sdk-8.0
                fi
            elif [ "$PACKAGE_MANAGER" = "dnf" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo dnf install -y dotnet-sdk-8.0
                fi
            elif [ "$PACKAGE_MANAGER" = "yum" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo yum install -y dotnet-sdk-8.0
                fi
            elif [ "$PACKAGE_MANAGER" = "pacman" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo pacman -S --noconfirm dotnet-sdk
                fi
            elif [ "$PACKAGE_MANAGER" = "zypper" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo zypper install -y dotnet-sdk-8.0
                fi
            elif [ "$PACKAGE_MANAGER" = "apk" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo apk add --no-cache dotnet8-sdk
                fi
            fi
        fi
        
        # Verify installation
        if command -v dotnet >/dev/null 2>&1; then
            echo -e "${GREEN}✓ .NET 8 SDK installed successfully ($(dotnet --version))${NC}"
        else
            echo -e "${RED}✗ Error: .NET SDK installation failed${NC}"
            echo -e "${YELLOW}Please install .NET 8 SDK manually from: https://dotnet.microsoft.com/download/dotnet/8.0${NC}"
            exit 1
        fi
    else
        echo -e "${GREEN}✓ .NET 8 SDK already installed ($(dotnet --version))${NC}"
    fi
}

# Check and install Node.js
check_and_install_nodejs() {
    local node_installed=false
    local node_version=""
    
    if command -v node >/dev/null 2>&1; then
        node_version=$(node --version 2>/dev/null | cut -d'v' -f2 | cut -d'.' -f1)
        if [ "$node_version" -ge "$REQUIRED_NODE_MAJOR_VERSION" ] 2>/dev/null; then
            node_installed=true
        fi
    fi
    
    if [ "$node_installed" = false ]; then
        echo -e "${YELLOW}Installing Node.js (LTS version)...${NC}"
        
        if [ "$OS" = "macos" ]; then
            brew install node
        elif [ "$OS" = "linux" ]; then
            # Install Node.js 20.x LTS
            if [ "$PACKAGE_MANAGER" = "apt" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://deb.nodesource.com/setup_20.x 2>/dev/null | sudo -E bash - || {
                    echo -e "${YELLOW}⚠ NodeSource setup failed, trying alternative method...${NC}"
                    sudo apt-get install -y nodejs npm
                }
                sudo apt-get install -y nodejs
            elif [ "$PACKAGE_MANAGER" = "dnf" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://rpm.nodesource.com/setup_20.x 2>/dev/null | sudo bash - || 
                sudo dnf install -y nodejs
            elif [ "$PACKAGE_MANAGER" = "yum" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://rpm.nodesource.com/setup_20.x 2>/dev/null | sudo bash - || 
                sudo yum install -y nodejs
            elif [ "$PACKAGE_MANAGER" = "pacman" ] && [ "$HAS_SUDO" = true ]; then
                sudo pacman -S --noconfirm nodejs npm
            elif [ "$PACKAGE_MANAGER" = "zypper" ] && [ "$HAS_SUDO" = true ]; then
                sudo zypper install -y nodejs npm
            elif [ "$PACKAGE_MANAGER" = "apk" ] && [ "$HAS_SUDO" = true ]; then
                sudo apk add --no-cache nodejs npm
            fi
        fi
        
        # Verify installation
        if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1; then
            echo -e "${GREEN}✓ Node.js installed successfully ($(node --version))${NC}"
            echo -e "${GREEN}✓ npm installed successfully (v$(npm --version))${NC}"
        else
            echo -e "${RED}✗ Error: Node.js installation failed${NC}"
            echo -e "${YELLOW}Please install Node.js 18+ manually from: https://nodejs.org/${NC}"
            exit 1
        fi
    else
        echo -e "${GREEN}✓ Node.js already installed ($(node --version))${NC}"
        
        # Check npm separately
        if ! command -v npm >/dev/null 2>&1; then
            echo -e "${YELLOW}npm not found. Installing npm...${NC}"
            if [ "$OS" = "macos" ]; then
                brew install npm
            elif [ "$OS" = "linux" ] && [ "$HAS_SUDO" = true ]; then
                if [ "$PACKAGE_MANAGER" = "pacman" ]; then
                    sudo pacman -S --noconfirm npm
                else
                    sudo $PACKAGE_MANAGER install -y npm
                fi
            fi
        fi
        if command -v npm >/dev/null 2>&1; then
            echo -e "${GREEN}✓ npm already installed (v$(npm --version))${NC}"
        fi
    fi
}

# Check and install required utilities
check_and_install_utilities() {
    # lsof (usually pre-installed on macOS/Linux)
    if ! command -v lsof >/dev/null 2>&1; then
        echo -e "${YELLOW}Installing lsof...${NC}"
        if [ "$OS" = "macos" ]; then
            # lsof is built-in on macOS
            echo -e "${YELLOW}lsof should be pre-installed on macOS${NC}"
        elif [ "$OS" = "linux" ] && [ "$HAS_SUDO" = true ]; then
            if [ "$PACKAGE_MANAGER" = "apt" ]; then
                sudo apt-get install -y lsof
            elif [ "$PACKAGE_MANAGER" = "dnf" ]; then
                sudo dnf install -y lsof
            elif [ "$PACKAGE_MANAGER" = "yum" ]; then
                sudo yum install -y lsof
            elif [ "$PACKAGE_MANAGER" = "pacman" ]; then
                sudo pacman -S --noconfirm lsof
            elif [ "$PACKAGE_MANAGER" = "zypper" ]; then
                sudo zypper install -y lsof
            elif [ "$PACKAGE_MANAGER" = "apk" ]; then
                sudo apk add --no-cache lsof
            fi
        fi
    fi
    
    # curl (usually pre-installed)
    if ! command -v curl >/dev/null 2>&1; then
        echo -e "${YELLOW}Installing curl...${NC}"
        if [ "$OS" = "macos" ]; then
            brew install curl
        elif [ "$OS" = "linux" ] && [ "$HAS_SUDO" = true ]; then
            if [ "$PACKAGE_MANAGER" = "pacman" ]; then
                sudo pacman -S --noconfirm curl
            elif [ "$PACKAGE_MANAGER" = "apk" ]; then
                sudo apk add --no-cache curl
            else
                sudo $PACKAGE_MANAGER install -y curl
            fi
        fi
    fi
    
    # wget (needed for some package installations)
    if ! command -v wget >/dev/null 2>&1 && [ "$OS" = "linux" ]; then
        echo -e "${YELLOW}Installing wget...${NC}"
        if [ "$HAS_SUDO" = true ]; then
            if [ "$PACKAGE_MANAGER" = "pacman" ]; then
                sudo pacman -S --noconfirm wget
            elif [ "$PACKAGE_MANAGER" = "apk" ]; then
                sudo apk add --no-cache wget
            elif [ "$PACKAGE_MANAGER" = "apt" ]; then
                sudo apt-get install -y wget
            else
                sudo $PACKAGE_MANAGER install -y wget 2>/dev/null || true
            fi
        fi
    fi
    
    echo -e "${GREEN}✓ System utilities verified${NC}"
}

# Restore .NET packages
restore_dotnet_packages() {
    echo -e "${YELLOW}Restoring .NET packages...${NC}"
    cd "$API_DIR"
    dotnet restore ServiceHub.sln
    echo -e "${GREEN}✓ .NET packages restored${NC}"
}

# Install npm packages
install_npm_packages() {
    if [ ! -d "$WEB_DIR/node_modules" ] || [ ! -f "$WEB_DIR/node_modules/.package-lock.json" ]; then
        echo -e "${YELLOW}Installing npm packages...${NC}"
        cd "$WEB_DIR"
        npm install
        echo -e "${GREEN}✓ npm packages installed${NC}"
    else
        echo -e "${GREEN}✓ npm packages already installed${NC}"
    fi
}

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

# ============================================================================
# MAIN EXECUTION
# ============================================================================

echo -e "${CYAN}╔════════════════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║         ServiceHub - Automated Setup & Launcher        ║${NC}"
echo -e "${CYAN}╚════════════════════════════════════════════════════════╝${NC}"
echo ""

# PHASE 0: DETECT OS AND INSTALL PREREQUISITES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║   Detecting System & Prerequisites     ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

detect_os
echo -e "${GREEN}✓ Detected OS: $OS ($PACKAGE_MANAGER)${NC}"
if [ "$OS" = "linux" ]; then
    echo -e "${CYAN}  Distribution: $DISTRO_NAME${NC}"
    [ "$IS_WSL" = true ] && echo -e "${CYAN}  ℹ Running under WSL${NC}"
fi
echo ""

install_homebrew

echo -e "${CYAN}Checking and installing prerequisites...${NC}"
echo -e "${CYAN}This may take a few minutes on first run.${NC}"
echo ""

check_and_install_dotnet
check_and_install_nodejs
check_and_install_utilities

echo ""
echo -e "${GREEN}✓ All prerequisites verified and installed${NC}"
echo ""

# PHASE 0.5: RESTORE DEPENDENCIES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Installing Project Dependencies    ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

restore_dotnet_packages
install_npm_packages

echo ""
echo -e "${GREEN}✓ All dependencies installed${NC}"
echo ""

# PHASE 1: AGGRESSIVE CLEANUP
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Cleaning Previous Sessions        ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# Kill all existing processes
echo -e "${YELLOW}Killing previous processes...${NC}"
if command -v pkill >/dev/null 2>&1; then
    pkill -f "dotnet.*ServiceHub" 2>/dev/null || true
    pkill -f "npm.*dev.*3000" 2>/dev/null || true
    pkill -f "npm.*dev.*5173" 2>/dev/null || true
    pkill -f "vite" 2>/dev/null || true
else
    # Fallback if pkill not available
    ps aux | grep -E "dotnet.*ServiceHub|npm.*dev.*3000|npm.*dev.*5173|vite" | grep -v grep | awk '{print $2}' | xargs kill 2>/dev/null || true
fi
sleep 1

# Force kill any stubborn processes on the ports
echo -e "${YELLOW}Force-closing ports 5153 and 3000...${NC}"
if command -v lsof >/dev/null 2>&1; then
    # Use xargs -r to avoid errors when no input (GNU xargs)
    # Use || true for BSD xargs which doesn't have -r
    lsof -ti:5153 2>/dev/null | xargs -r kill -9 2>/dev/null || lsof -ti:5153 2>/dev/null | xargs kill -9 2>/dev/null || true
    lsof -ti:3000 2>/dev/null | xargs -r kill -9 2>/dev/null || lsof -ti:3000 2>/dev/null | xargs kill -9 2>/dev/null || true
    lsof -ti:5173 2>/dev/null | xargs -r kill -9 2>/dev/null || lsof -ti:5173 2>/dev/null | xargs kill -9 2>/dev/null || true
else
    echo -e "${YELLOW}⚠ lsof not available, skipping port cleanup${NC}"
fi
sleep 2

# Clean temporary files and logs
echo -e "${YELLOW}Cleaning temporary files...${NC}"
rm -f /tmp/servicehub_api.log 2>/dev/null || true
rm -f /tmp/servicehub_ui.log 2>/dev/null || true
rm -f /tmp/servicehub_*.log 2>/dev/null || true

# Clean API build artifacts if needed
echo -e "${YELLOW}Cleaning API build artifacts...${NC}"
cd "$API_DIR"
find . \( -type d -name "bin" -o -name "obj" \) 2>/dev/null | head -5 | while IFS= read -r dir; do
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

# PHASE 2: VERIFY DIRECTORIES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Verifying Project Structure       ║${NC}"
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

echo ""
echo -e "${GREEN}✓ Project structure verified${NC}"
echo ""

# PHASE 3: PORT VERIFICATION
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Verifying Ports Available         ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

for PORT in 5153 $WEB_PORT; do
    if command -v lsof >/dev/null 2>&1; then
        PID=$(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null || true)
        if [ -n "$PID" ]; then
            echo -e "${YELLOW}⚠ Port $PORT in use (PID: $PID). Force-stopping...${NC}"
            kill -9 $PID 2>/dev/null || true
            sleep 1
        else
            echo -e "${GREEN}✓ Port $PORT available${NC}"
        fi
    else
        echo -e "${YELLOW}⚠ Cannot check port $PORT (lsof not available)${NC}"
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
