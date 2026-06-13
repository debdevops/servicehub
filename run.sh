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

# Parse command-line arguments
SIMULATOR_MODE=false
for arg in "$@"; do
  case $arg in
    --simulator)
      SIMULATOR_MODE=true
      ;;
    --help|-h)
      echo "Usage: ./run.sh [--simulator]"
      echo ""
      echo "  --simulator   Start the API in Simulator mode with seeded demo data."
      echo "                No real Azure/AWS/GCP credentials required."
      echo "                API runs on http://localhost:5200"
      echo "                UI runs on http://localhost:3000"
      echo ""
      echo "  (no flags)    Start in Development mode (requires credentials)."
      echo "                API runs on http://localhost:5153"
      echo "                UI runs on http://localhost:3000"
      exit 0
      ;;
  esac
done

# Version requirements
REQUIRED_DOTNET_VERSION="10.0"
REQUIRED_NODE_MAJOR_VERSION="20"

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

# Check system connectivity before long operations
check_connectivity() {
    if ! command -v curl >/dev/null 2>&1; then
        echo -e "${YELLOW}⚠ Warning: curl not found. Some downloads may fail.${NC}"
        return 0  # Don't fail here, let tools try anyway
    fi
    
    if ! curl -s --connect-timeout 5 --max-time 5 https://www.google.com >/dev/null 2>&1; then
        echo -e "${YELLOW}⚠ Warning: Internet connectivity check failed. Some downloads may not work.${NC}"
    fi
}

# Check macOS Xcode Command Line Tools
check_xcode_clt() {
    if [ "$OS" = "macos" ]; then
        if ! xcode-select -p >/dev/null 2>&1; then
            echo -e "${YELLOW}Installing Xcode Command Line Tools (required for npm packages)...${NC}"
            xcode-select --install
            echo -e "${YELLOW}Please complete the Xcode CLT installation, then re-run this script.${NC}"
            exit 1
        fi
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
            echo -e "${YELLOW}Please install .NET 10 SDK and Node.js 20+ manually${NC}"
            exit 1
            ;;
        CYGWIN*|MINGW*|MSYS*)
            echo -e "${RED}✗ Native Windows detected. run.sh requires WSL or bash.${NC}"
            echo ""
            echo -e "${YELLOW}Option 1 (recommended): Use WSL${NC}"
            echo -e "  wsl --install"
            echo -e "  Then re-run: ./run.sh"
            echo ""
            echo -e "${YELLOW}Option 2: Use PowerShell${NC}"
            echo -e "  A PowerShell equivalent is available: .\\run.ps1"
            echo ""
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

# Install .NET 10 via Microsoft's official install script (works on any Linux distro)
install_dotnet_via_script() {
    echo -e "${YELLOW}  Trying Microsoft .NET install script (works on any distro)...${NC}"
    local install_dir="${DOTNET_ROOT:-$HOME/.dotnet}"
    local install_script="/tmp/dotnet-install.sh"

    if ! command -v curl >/dev/null 2>&1; then
        echo -e "${RED}  ✗ curl is required to download the .NET install script${NC}"
        return 1
    fi

    if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$install_script" 2>/dev/null; then
        echo -e "${RED}  ✗ Failed to download dotnet-install.sh${NC}"
        return 1
    fi

    if [ ! -f "$install_script" ]; then
        echo -e "${RED}  ✗ dotnet-install.sh not found after download${NC}"
        return 1
    fi

    chmod +x "$install_script"
    if ! bash "$install_script" --channel 10.0 --install-dir "$install_dir" 2>/dev/null; then
        echo -e "${RED}  ✗ .NET installation script failed${NC}"
        rm -f "$install_script"
        return 1
    fi
    rm -f "$install_script"

    # Ensure PATH is updated for this script execution
    export DOTNET_ROOT="$install_dir"
    export PATH="$install_dir:$PATH"

    # Verify installation
    if command -v dotnet >/dev/null 2>&1 && [ "$(dotnet --version 2>/dev/null | cut -d'.' -f1)" = "10" ]; then
        echo -e "${GREEN}  ✓ .NET 10 SDK installed to $install_dir ($(dotnet --version))${NC}"
        echo -e "${CYAN}  ℹ To persist for future shells, add to ~/.bashrc or ~/.zshrc:${NC}"
        echo -e "    export DOTNET_ROOT=\"$install_dir\""
        echo -e "    export PATH=\"\$DOTNET_ROOT:\$PATH\""
        return 0
    else
        echo -e "${RED}  ✗ .NET 10 verification failed after installation${NC}"
        return 1
    fi
}

# Check and install .NET SDK
check_and_install_dotnet() {
    local dotnet_installed=false
    local dotnet_version=""

    if command -v dotnet >/dev/null 2>&1; then
        dotnet_version=$(dotnet --version 2>/dev/null | cut -d'.' -f1)
        if [ "$dotnet_version" = "10" ]; then
            dotnet_installed=true
        fi
    fi

    if [ "$dotnet_installed" = false ]; then
        # Show what's currently installed (if anything) to help the user understand
        if command -v dotnet >/dev/null 2>&1; then
            echo -e "${YELLOW}Installing .NET 10 SDK... (found $(dotnet --version), need 10.x)${NC}"
        else
            echo -e "${YELLOW}Installing .NET 10 SDK...${NC}"
        fi

        if [ "$OS" = "macos" ]; then
            brew install --cask dotnet-sdk

        elif [ "$OS" = "linux" ]; then
            local pkg_install_ok=false

            if [ "$PACKAGE_MANAGER" = "apt" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    # Detect Ubuntu/Debian version
                    if [ -f /etc/os-release ]; then
                        . /etc/os-release
                        VERSION_NUM="${VERSION_ID:-22.04}"
                    else
                        VERSION_NUM="22.04"
                    fi
                    # Try adding Microsoft APT repo + installing
                    (
                        wget -q "https://packages.microsoft.com/config/ubuntu/${VERSION_NUM}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb 2>/dev/null ||
                        wget -q "https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb
                        sudo dpkg -i /tmp/packages-microsoft-prod.deb
                        rm -f /tmp/packages-microsoft-prod.deb
                        sudo apt-get update -q
                        sudo apt-get install -y dotnet-sdk-10.0
                    ) && pkg_install_ok=true || pkg_install_ok=false
                fi

            elif [ "$PACKAGE_MANAGER" = "dnf" ] || [ "$PACKAGE_MANAGER" = "yum" ]; then
                # .NET 10 may not be in distro repos yet (e.g. RHEL 8/9 ships .NET 8).
                # Try the package manager first; fall through to the install script on failure.
                if [ "$HAS_SUDO" = true ]; then
                    echo -e "${YELLOW}  Trying $PACKAGE_MANAGER install dotnet-sdk-10.0...${NC}"
                    if sudo "$PACKAGE_MANAGER" install -y dotnet-sdk-10.0 2>/dev/null; then
                        pkg_install_ok=true
                    else
                        echo -e "${YELLOW}  dotnet-sdk-10.0 not found in distro repos (this is normal on RHEL/CentOS — .NET 10 is new).${NC}"
                        pkg_install_ok=false
                    fi
                fi

            elif [ "$PACKAGE_MANAGER" = "pacman" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo pacman -S --noconfirm dotnet-sdk && pkg_install_ok=true || pkg_install_ok=false
                fi

            elif [ "$PACKAGE_MANAGER" = "zypper" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo zypper install -y dotnet-sdk-10.0 && pkg_install_ok=true || pkg_install_ok=false
                fi

            elif [ "$PACKAGE_MANAGER" = "apk" ]; then
                if [ "$HAS_SUDO" = true ]; then
                    sudo apk add --no-cache dotnet10-sdk && pkg_install_ok=true || pkg_install_ok=false
                fi
            fi

            # Fallback: Microsoft's universal install script (works on any distro/version)
            if [ "$pkg_install_ok" = false ]; then
                install_dotnet_via_script || {
                    echo -e "${RED}✗ Could not install .NET 10 SDK automatically.${NC}"
                    echo -e "${YELLOW}Please install it manually using one of:${NC}"
                    echo -e "  1. Microsoft install script:  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0"
                    echo -e "  2. Download directly:         https://dotnet.microsoft.com/download/dotnet/10.0"
                    echo -e "  3. On RHEL, add the Microsoft repo first:"
                    echo -e "       sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc"
                    echo -e "       sudo dnf install -y https://packages.microsoft.com/config/rhel/9/packages-microsoft-prod.rpm"
                    echo -e "       sudo dnf install -y dotnet-sdk-10.0"
                    exit 1
                }
            fi
        fi

        # Verify installation
        if command -v dotnet >/dev/null 2>&1 && [ "$(dotnet --version 2>/dev/null | cut -d'.' -f1)" = "10" ]; then
            echo -e "${GREEN}✓ .NET 10 SDK ready ($(dotnet --version))${NC}"
        else
            echo -e "${RED}✗ Error: .NET 10 SDK installation failed or wrong version installed.${NC}"
            echo -e "${YELLOW}Installed: $(dotnet --version 2>/dev/null || echo 'none') — required: 10.x${NC}"
            echo ""
            echo -e "${YELLOW}Manual installation options:${NC}"
            echo -e "  macOS:    brew install --cask dotnet-sdk"
            echo -e "  Ubuntu:   sudo apt-get install -y dotnet-sdk-10.0"
            echo -e "  RHEL:     sudo dnf install -y dotnet-sdk-10.0"
            echo -e "  Arch:     sudo pacman -S dotnet-sdk"
            echo -e "  Windows:  winget install Microsoft.DotNet.SDK.10"
            echo -e "  Manual:   https://dotnet.microsoft.com/download/dotnet/10.0"
            exit 1
        fi
    else
        echo -e "${GREEN}✓ .NET 10 SDK already installed ($(dotnet --version))${NC}"
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
            echo ""
            echo -e "${YELLOW}Manual installation options:${NC}"
            echo -e "  macOS:    brew install node"
            echo -e "  Ubuntu:   curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash - && sudo apt-get install -y nodejs"
            echo -e "  RHEL:     curl -fsSL https://rpm.nodesource.com/setup_20.x | sudo bash -"
            echo -e "  Arch:     sudo pacman -S nodejs npm"
            echo -e "  Windows:  winget install OpenJS.NodeJS.LTS"
            echo -e "  Manual:   https://nodejs.org/"
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
        
        # Use npm ci (clean install) if package-lock.json exists for reproducible builds
        if [ -f "package-lock.json" ]; then
            npm ci --legacy-peer-deps 2>&1 | tail -5
            if [ ${PIPESTATUS[0]} -ne 0 ]; then
                echo -e "${YELLOW}npm ci failed, trying npm install as fallback...${NC}"
                npm install --legacy-peer-deps || {
                    echo -e "${RED}✗ Error: npm package installation failed${NC}"
                    exit 1
                }
            fi
        else
            npm install --legacy-peer-deps || {
                echo -e "${RED}✗ Error: npm package installation failed${NC}"
                exit 1
            }
        fi
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

# Verify we are running in Development mode (never Production locally)
if [ "${ASPNETCORE_ENVIRONMENT:-Development}" = "Production" ]; then
    echo -e "${YELLOW}⚠ Warning: ASPNETCORE_ENVIRONMENT is set to Production.${NC}"
    echo -e "   run.sh is intended for local development only."
    echo -e "   For Azure deployment, use the self-hosting guide: ./self-hosting/README.md${NC}"
    read -r -p "Continue anyway? (y/N): " confirm
    [[ "$confirm" =~ ^[Yy]$ ]] || exit 0
fi

# Pre-flight checks
echo -e "${YELLOW}Running pre-flight checks...${NC}"
check_connectivity

detect_os
echo -e "${GREEN}✓ Detected OS: $OS ($PACKAGE_MANAGER)${NC}"
if [ "$OS" = "linux" ]; then
    echo -e "${CYAN}  Distribution: $DISTRO_NAME${NC}"
    [ "$IS_WSL" = true ] && echo -e "${CYAN}  ℹ Running under WSL${NC}"
fi
echo ""

check_xcode_clt

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

# PHASE 0.75: GENERATE LOCAL SECRETS
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║      Generating Local Secrets          ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# NOTE: appsettings.Local.json is git-ignored and only loaded in Development mode.
#       It does NOT affect Azure App Service deployments.
#       Azure uses Application Settings (env vars) from appsettings.Production.json instead.
LOCAL_SETTINGS="$SCRIPT_DIR/services/api/src/ServiceHub.Api/appsettings.Local.json"
if [[ -f "$LOCAL_SETTINGS" ]]; then
    echo -e "${GREEN}✓ appsettings.Local.json already exists — keeping existing secrets${NC}"
else
    KEYS_SCRIPT="$SCRIPT_DIR/scripts/generate-keys.sh"
    if [ -f "$KEYS_SCRIPT" ]; then
        if bash "$KEYS_SCRIPT" --local 2>&1; then
            echo -e "${GREEN}✓ Secrets generated successfully${NC}"
        else
            echo -e "${YELLOW}⚠ Warning: Secrets generation had issues, but continuing${NC}"
        fi
    else
        echo -e "${YELLOW}⚠ Warning: generate-keys.sh not found, skipping secret generation${NC}"
    fi
fi
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
    # Kill processes on port 5153
    PIDS=$(lsof -ti:5153 2>/dev/null || true)
    if [ -n "$PIDS" ]; then
        echo "$PIDS" | xargs kill -9 2>/dev/null || echo "$PIDS" | xargs -x kill -9 2>/dev/null || true
    fi
    
    # Kill processes on port 3000
    PIDS=$(lsof -ti:3000 2>/dev/null || true)
    if [ -n "$PIDS" ]; then
        echo "$PIDS" | xargs kill -9 2>/dev/null || echo "$PIDS" | xargs -x kill -9 2>/dev/null || true
    fi
    
    # Kill processes on port 5173
    PIDS=$(lsof -ti:5173 2>/dev/null || true)
    if [ -n "$PIDS" ]; then
        echo "$PIDS" | xargs kill -9 2>/dev/null || echo "$PIDS" | xargs -x kill -9 2>/dev/null || true
    fi
else
    echo -e "${YELLOW}⚠ lsof not available, skipping port cleanup${NC}"
fi
sleep 2

# Clean temporary files and logs
echo -e "${YELLOW}Cleaning temporary files...${NC}"
rm -f /tmp/servicehub_api.log 2>/dev/null || true
rm -f /tmp/servicehub_ui.log 2>/dev/null || true
rm -f /tmp/servicehub_*.log 2>/dev/null || true

# Clean Vite cache (quick, avoids stale HMR state)
echo -e "${YELLOW}Cleaning Vite cache...${NC}"
rm -rf "$WEB_DIR/node_modules/.vite" 2>/dev/null || true

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

if [ "$SIMULATOR_MODE" = true ]; then
  PORTS_TO_CLEAN="5200 $WEB_PORT"
else
  PORTS_TO_CLEAN="5153 $WEB_PORT"
fi
for PORT in $PORTS_TO_CLEAN; do
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
if [ "$SIMULATOR_MODE" = true ]; then
  echo -e "${YELLOW}⚗️  Starting in SIMULATOR MODE — no real cloud credentials needed${NC}"
  echo -e "${CYAN}   Seeding 3 namespaces: Azure (contoso) + AWS (acme) + GCP (globex)${NC}"
  echo ""
  export ASPNETCORE_ENVIRONMENT=Simulator
  dotnet run \
    --project "$API_DIR/src/ServiceHub.Api/ServiceHub.Api.csproj" \
    --no-launch-profile \
    --urls http://localhost:5200 \
    > /tmp/servicehub_api_startup.log 2>&1 &
  API_PID=$!
  API_HTTP_URL="http://localhost:5200"
  HEALTH_URL="http://localhost:5200/health"
else
  # Ensure environment variables are set for .NET
  export ASPNETCORE_ENVIRONMENT=Development
  export ASPNETCORE_URLS="http://localhost:5153"
  ASPNETCORE_ENVIRONMENT=Development bash run-api.sh > /tmp/servicehub_api_startup.log 2>&1 &
  API_PID=$!
  HEALTH_URL="http://localhost:5153/health"
fi
echo -e "${GREEN}✓ API process started (PID: $API_PID)${NC}"

# Wait for API to be ready (max 30 seconds on first run)
echo -e "${YELLOW}Waiting for API to be ready...${NC}"
WAIT_COUNT=0
MAX_WAIT=30
API_READY=false
API_ERROR=""
while [ $WAIT_COUNT -lt $MAX_WAIT ]; do
    if curl -s $HEALTH_URL >/dev/null 2>&1; then
        API_READY=true
        break
    fi
    
    # Check if process died
    if ! kill -0 $API_PID 2>/dev/null; then
        API_ERROR="API process died (PID $API_PID)"
        break
    fi
    
    # Show progress every 5 seconds
    if [ $((WAIT_COUNT % 5)) -eq 0 ]; then
        printf "  .."
    fi
    sleep 1
    WAIT_COUNT=$((WAIT_COUNT + 1))
done

if [ "$API_READY" = true ]; then
    echo -e " ${GREEN}✓ API is ready (${WAIT_COUNT}s)${NC}"
elif [ -n "$API_ERROR" ]; then
    echo -e " ${RED}✗ $API_ERROR${NC}"
    echo -e "${YELLOW}API startup log:${NC}"
    head -20 /tmp/servicehub_api_startup.log 2>/dev/null || echo "  (no log available)"
    cleanup
else
    echo -e " ${YELLOW}⚠ API startup check timed out after ${MAX_WAIT}s${NC}"
    if [ -f /tmp/servicehub_api_startup.log ] && [ -s /tmp/servicehub_api_startup.log ]; then
        echo -e "${YELLOW}Last 5 API log entries:${NC}"
        tail -5 /tmp/servicehub_api_startup.log | sed 's/^/  /'
    fi
    echo -e "${YELLOW}Continuing anyway... (check $HEALTH_URL manually)${NC}"
fi

echo ""

# Start Web UI in background
echo -e "${BLUE}Starting UI...${NC}"
cd "$WEB_DIR"
npm run dev -- --port $WEB_PORT --host 0.0.0.0 --strictPort > /tmp/servicehub_ui_startup.log 2>&1 &
WEB_PID=$!
echo -e "${GREEN}✓ UI process started (PID: $WEB_PID)${NC}"

# Wait for UI to be ready — use port check instead of curl (Vite dev server
# keeps connections open and curl hangs waiting for a response on macOS)
echo -e "${YELLOW}Waiting for UI to be ready...${NC}"
WAIT_COUNT=0
MAX_WAIT=30
UI_READY=false
while [ $WAIT_COUNT -lt $MAX_WAIT ]; do
    if command -v lsof >/dev/null 2>&1; then
        if lsof -nP -iTCP:$WEB_PORT -sTCP:LISTEN -t >/dev/null 2>&1; then
            UI_READY=true
            break
        fi
    else
        # Fallback: give it time to start
        if [ $WAIT_COUNT -ge 10 ]; then
            UI_READY=true
            break
        fi
    fi
    # Show progress every 5 seconds
    if [ $((WAIT_COUNT % 5)) -eq 0 ]; then
        printf "  .."
    fi
    sleep 1
    WAIT_COUNT=$((WAIT_COUNT + 1))
done

if [ "$UI_READY" = true ]; then
    echo -e " ${GREEN}✓ UI is ready (${WAIT_COUNT}s)${NC}"
else
    echo -e " ${YELLOW}⚠ UI startup check timed out after ${MAX_WAIT}s${NC}"
    if [ -f /tmp/servicehub_ui_startup.log ]; then
        echo -e "${YELLOW}Last UI log entries:${NC}"
        tail -3 /tmp/servicehub_ui_startup.log || true
    fi
fi

echo ""

# PHASE 5: SERVICES READY
# Detect server IP and hostname for remote access guidance
SERVER_IP=""
SERVER_HOSTNAME=$(hostname 2>/dev/null || echo "")

# Try multiple methods to get server IP (hostname -I doesn't work on macOS)
if command -v hostname >/dev/null 2>&1; then
    # Try hostname -I (Linux)
    SERVER_IP=$(hostname -I 2>/dev/null | awk '{print $1}' || true)
fi

# Fallback: use ip command (Linux, WSL)
if [ -z "$SERVER_IP" ] && command -v ip >/dev/null 2>&1; then
    SERVER_IP=$(ip route get 1 2>/dev/null | awk '{print $NF; exit}' || true)
fi

# Fallback: use ifconfig (macOS, BSD)
if [ -z "$SERVER_IP" ] && command -v ifconfig >/dev/null 2>&1; then
    SERVER_IP=$(ifconfig 2>/dev/null | grep -E 'inet[^6]' | grep -v '127.0.0.1' | head -1 | awk '{print $2}' || true)
fi

echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
if [ "$SIMULATOR_MODE" = true ]; then
echo -e "${YELLOW}║  ⚗️  Simulator Mode — All Services Up!  ║${NC}"
else
echo -e "${GREEN}║   ✓ All Services Running Successfully!  ║${NC}"
fi
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""
if [ "$SIMULATOR_MODE" = true ]; then
echo -e "${CYAN}⚗️  No real credentials needed. Using seeded demo data.${NC}"
echo -e "${CYAN}   Azure (contoso) · AWS (acme) · GCP (globex)${NC}"
echo ""
fi
echo -e "${BLUE}📍 API Endpoints:${NC}"
echo -e "  • ${GREEN}HTTP:  ${API_HTTP_URL}${NC}"
if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" != "127.0.0.1" ] && [ "$SIMULATOR_MODE" != true ]; then
    echo -e "  • ${GREEN}Remote: http://${SERVER_IP}:5153${NC}"
fi
echo -e "  • ${GREEN}Docs:   ${API_HTTP_URL}/scalar/v1${NC}"
echo ""
echo -e "${BLUE}🌐 Web UI:${NC}"
echo -e "  • ${GREEN}http://localhost:${WEB_PORT}${NC}   ← from this machine"
if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" != "127.0.0.1" ]; then
    echo -e "  • ${GREEN}http://${SERVER_IP}:${WEB_PORT}${NC}   ← from remote machines (by IP)"
fi
if [ -n "$SERVER_HOSTNAME" ] && [ "$SERVER_HOSTNAME" != "localhost" ]; then
    echo -e "  • ${GREEN}http://${SERVER_HOSTNAME}:${WEB_PORT}${NC}   ← from remote machines (by hostname)"
fi
echo ""
echo -e "${BLUE}📋 Process IDs:${NC}"
echo -e "  • ${GREEN}API:  $API_PID${NC}"
echo -e "  • ${GREEN}UI:   $WEB_PID${NC}"
echo ""
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo -e "${BLUE}Press ${YELLOW}Ctrl+C${BLUE} to stop all services${NC}"
echo -e "${YELLOW}════════════════════════════════════════${NC}"
echo ""
if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" != "127.0.0.1" ]; then
    echo -e "${CYAN}ℹ  Remote access detected. If connection is refused from another machine:${NC}"
    echo -e "   ${YELLOW}Linux (UFW):${NC}    sudo ufw allow 3000/tcp && sudo ufw allow 5153/tcp && sudo ufw reload"
    echo -e "   ${YELLOW}Linux (Firewall):${NC} sudo firewall-cmd --add-port=3000/tcp --permanent && sudo firewall-cmd --add-port=5153/tcp --permanent && sudo firewall-cmd --reload"
    echo -e "   ${YELLOW}Windows (PowerShell):${NC} New-NetFirewallRule -DisplayName 'ServiceHub API' -Direction Inbound -LocalPort 5153 -Action Allow -Protocol tcp"
    echo -e "   ${YELLOW}macOS:${NC}          Check System Preferences → Security & Privacy → Firewall"
    echo -e ""
fi

# Keep services running
wait
