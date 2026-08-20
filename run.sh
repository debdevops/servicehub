#!/usr/bin/env bash

# ServiceHub - Full Stack Development Server Runner
# Supports multiple applications: ServiceHub, Demo, Sandbox
# Automatically installs prerequisites and starts requested services
# Supports: macOS, Ubuntu/Debian, RHEL/CentOS/Fedora, Arch Linux, openSUSE, WSL

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_DIR="$SCRIPT_DIR/services/api"
WEB_DIR="$SCRIPT_DIR/apps/web"
DEMO_DIR="$SCRIPT_DIR/apps/demo"
SANDBOX_DIR="$SCRIPT_DIR/apps/sandbox"
API_HTTP_URL="http://localhost:5153"
API_HTTPS_URL="https://localhost:7252"
WEB_PORT=3000

# Version requirements
REQUIRED_DOTNET_VERSION="10.0"
REQUIRED_NODE_MAJOR_VERSION="22"

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

# Show help message
show_help() {
  cat <<'HELP'
Usage: ./run.sh [command] [options]

Commands:
  servicehub                Start ServiceHub API + Web UI (default)
  demo                      Start Demo application only (experimental, unsupported — see apps/demo/README.md)
  sandbox                   Start Sandbox application only (experimental, unsupported — see apps/sandbox/README.md)
  all                       Start ServiceHub API, Web UI, Demo, and Sandbox

Examples:
  ./run.sh                  # Start ServiceHub (same as ./run.sh servicehub)
  ./run.sh servicehub       # Start ServiceHub API + Web UI
  ./run.sh demo             # Start Demo only (experimental)
  ./run.sh --help           # Show this help message

Press Ctrl+C to stop all services.
HELP
}

# Process PIDs for cleanup
declare -a PIDS

# Parse command-line arguments
START_API=false
START_WEB=false
START_DEMO=false
START_SANDBOX=false
COMMAND="${1:-servicehub}"

case "$COMMAND" in
  servicehub)
    START_API=true
    START_WEB=true
    shift || true
    for arg in "$@"; do
      case $arg in
        --help|-h)
          show_help
          exit 0
          ;;
      esac
    done
    ;;
  demo)
    START_DEMO=true
    shift || true
    for arg in "$@"; do
      case $arg in
        --help|-h)
          show_help
          exit 0
          ;;
      esac
    done
    ;;
  sandbox)
    START_SANDBOX=true
    shift || true
    for arg in "$@"; do
      case $arg in
        --help|-h)
          show_help
          exit 0
          ;;
      esac
    done
    ;;
  all)
    START_API=true
    START_WEB=true
    START_DEMO=true
    START_SANDBOX=true
    shift || true
    for arg in "$@"; do
      case $arg in
        --help|-h)
          show_help
          exit 0
          ;;
      esac
    done
    ;;
  --help|-h)
    show_help
    exit 0
    ;;
  *)
    echo -e "${RED}✗ Unknown command: $COMMAND${NC}"
    show_help
    exit 1
    ;;
esac

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
            echo -e "${YELLOW}Please install .NET 10 SDK and Node.js 22+ manually${NC}"
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
            # Install Node.js 22.x LTS
            if [ "$PACKAGE_MANAGER" = "apt" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://deb.nodesource.com/setup_22.x 2>/dev/null | sudo -E bash - || {
                    echo -e "${YELLOW}⚠ NodeSource setup failed, trying alternative method...${NC}"
                    sudo apt-get install -y nodejs npm
                }
                sudo apt-get install -y nodejs
            elif [ "$PACKAGE_MANAGER" = "dnf" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://rpm.nodesource.com/setup_22.x 2>/dev/null | sudo bash - || 
                sudo dnf install -y nodejs
            elif [ "$PACKAGE_MANAGER" = "yum" ] && [ "$HAS_SUDO" = true ]; then
                curl -fsSL https://rpm.nodesource.com/setup_22.x 2>/dev/null | sudo bash - || 
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
            echo -e "  Ubuntu:   curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - && sudo apt-get install -y nodejs"
            echo -e "  RHEL:     curl -fsSL https://rpm.nodesource.com/setup_22.x | sudo bash -"
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

# Install npm packages (monorepo workspaces)
install_npm_packages() {
    if [ ! -d "$SCRIPT_DIR/node_modules" ] || [ ! -f "$SCRIPT_DIR/node_modules/.package-lock.json" ]; then
        echo -e "${YELLOW}Installing npm packages (monorepo workspaces)...${NC}"
        cd "$SCRIPT_DIR"

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
        echo -e "${GREEN}✓ npm packages installed (all workspaces)${NC}"
    else
        echo -e "${GREEN}✓ npm packages already installed${NC}"
    fi
}

cleanup() {
    echo ""
    echo -e "${YELLOW}Shutting down services...${NC}"

    # Kill all tracked processes
    for pid in "${PIDS[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
        fi
    done

    # Wait for all processes to terminate gracefully (max 5 seconds)
    local count=0
    while [ ${#PIDS[@]} -gt 0 ] && [ $count -lt 5 ]; do
        local alive=()
        for pid in "${PIDS[@]}"; do
            if kill -0 "$pid" 2>/dev/null; then
                alive+=("$pid")
            fi
        done
        PIDS=("${alive[@]}")
        if [ ${#PIDS[@]} -gt 0 ]; then
            sleep 1
            count=$((count + 1))
        fi
    done

    # Force kill any remaining processes
    for pid in "${PIDS[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then
            kill -9 "$pid" 2>/dev/null || true
        fi
    done

    echo -e "${GREEN}✓ All services stopped${NC}"
    exit 0
}

# Trap SIGINT, SIGTERM, EXIT to cleanup gracefully
trap cleanup SIGINT SIGTERM EXIT

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
    echo -e "   For a real deployment, see the Quick Start and Security sections in README.md${NC}"
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
    pkill -f "npm.*dev" 2>/dev/null || true
    pkill -f "vite" 2>/dev/null || true
else
    # Fallback if pkill not available
    ps aux | grep -E "dotnet.*ServiceHub|npm.*dev|vite" | grep -v grep | awk '{print $2}' | xargs kill 2>/dev/null || true
fi
sleep 1

# Force kill any stubborn processes on the ports
PORTS_TO_CLEAN="5153 3000 5173"
if [ "$START_DEMO" = true ]; then
    PORTS_TO_CLEAN="$PORTS_TO_CLEAN 5174"
fi
if [ "$START_SANDBOX" = true ]; then
    PORTS_TO_CLEAN="$PORTS_TO_CLEAN 5175"
fi

echo -e "${YELLOW}Force-closing ports: $PORTS_TO_CLEAN...${NC}"
if command -v lsof >/dev/null 2>&1; then
    for PORT in $PORTS_TO_CLEAN; do
        PIDS=$(lsof -ti:$PORT 2>/dev/null || true)
        if [ -n "$PIDS" ]; then
            echo "$PIDS" | xargs kill -9 2>/dev/null || echo "$PIDS" | xargs -x kill -9 2>/dev/null || true
        fi
    done
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

PORTS_TO_VERIFY=""
if [ "$START_API" = true ]; then
    PORTS_TO_VERIFY="$PORTS_TO_VERIFY 5153"
fi
if [ "$START_WEB" = true ]; then
    PORTS_TO_VERIFY="$PORTS_TO_VERIFY 3000"
fi
if [ "$START_DEMO" = true ]; then
    PORTS_TO_VERIFY="$PORTS_TO_VERIFY 5174"
fi
if [ "$START_SANDBOX" = true ]; then
    PORTS_TO_VERIFY="$PORTS_TO_VERIFY 5175"
fi

for PORT in $PORTS_TO_VERIFY; do
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

# Helper function to start a service and wait for it to be ready
start_service() {
    local name=$1
    local port=$2
    local health_url=$3
    local start_cmd=$4
    local log_file=$5

    echo -e "${BLUE}Starting $name...${NC}"
    eval "$start_cmd" > "$log_file" 2>&1 &
    local pid=$!
    PIDS+=("$pid")
    echo -e "${GREEN}✓ $name process started (PID: $pid)${NC}"

    if [ -n "$health_url" ]; then
        echo -e "${YELLOW}Waiting for $name to be ready...${NC}"
        local wait_count=0
        local max_wait=30
        local ready=false

        while [ $wait_count -lt $max_wait ]; do
            if curl -s "$health_url" >/dev/null 2>&1; then
                ready=true
                break
            fi

            if ! kill -0 $pid 2>/dev/null; then
                echo -e " ${RED}✗ $name process died (PID $pid)${NC}"
                echo -e "${YELLOW}$name startup log:${NC}"
                head -20 "$log_file" 2>/dev/null || echo "  (no log available)"
                cleanup
            fi

            if [ $((wait_count % 5)) -eq 0 ]; then
                printf "  .."
            fi
            sleep 1
            wait_count=$((wait_count + 1))
        done

        if [ "$ready" = true ]; then
            echo -e " ${GREEN}✓ $name is ready (${wait_count}s)${NC}"
        else
            echo -e " ${YELLOW}⚠ $name startup check timed out after ${max_wait}s${NC}"
            if [ -f "$log_file" ] && [ -s "$log_file" ]; then
                echo -e "${YELLOW}Last 5 $name log entries:${NC}"
                tail -5 "$log_file" | sed 's/^/  /'
            fi
            echo -e "${YELLOW}Continuing anyway... (check $health_url manually)${NC}"
        fi
    else
        # Port-based readiness check (for Vite/npm apps)
        echo -e "${YELLOW}Waiting for $name to be ready...${NC}"
        local wait_count=0
        local max_wait=30
        local ready=false

        while [ $wait_count -lt $max_wait ]; do
            if command -v lsof >/dev/null 2>&1; then
                if lsof -nP -iTCP:$port -sTCP:LISTEN -t >/dev/null 2>&1; then
                    ready=true
                    break
                fi
            else
                if [ $wait_count -ge 10 ]; then
                    ready=true
                    break
                fi
            fi

            if [ $((wait_count % 5)) -eq 0 ]; then
                printf "  .."
            fi
            sleep 1
            wait_count=$((wait_count + 1))
        done

        if [ "$ready" = true ]; then
            echo -e " ${GREEN}✓ $name is ready (${wait_count}s)${NC}"
        else
            echo -e " ${YELLOW}⚠ $name startup check timed out after ${max_wait}s${NC}"
            if [ -f "$log_file" ]; then
                echo -e "${YELLOW}Last $name log entries:${NC}"
                tail -3 "$log_file" || true
            fi
        fi
    fi

    echo ""
}

# PHASE 4: START SERVICES
echo -e "${YELLOW}╔════════════════════════════════════════╗${NC}"
echo -e "${YELLOW}║        Starting Services              ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

# Start API if requested
if [ "$START_API" = true ]; then
    HEALTH_URL="http://localhost:5153/health"
    start_service "API" "5153" "$HEALTH_URL" \
        "cd $API_DIR && export ASPNETCORE_ENVIRONMENT=Development && export ASPNETCORE_URLS=http://localhost:5153 && bash run-api.sh" \
        "/tmp/servicehub_api_startup.log"
fi

# Start Web UI if requested
if [ "$START_WEB" = true ]; then
    start_service "Web UI" "3000" "" \
        "cd $SCRIPT_DIR && export VITE_PROXY_TARGET=$API_HTTP_URL && npm run -w apps/web dev -- --port 3000 --host 0.0.0.0 --strictPort" \
        "/tmp/servicehub_ui_startup.log"
fi

# Start Demo if requested
if [ "$START_DEMO" = true ]; then
    if [ ! -d "$DEMO_DIR" ]; then
        echo -e "${YELLOW}⚠ Demo directory not found at $DEMO_DIR${NC}"
    else
        start_service "Demo" "5174" "" \
            "cd $SCRIPT_DIR && npm run -w apps/demo dev -- --port 5174 --host 0.0.0.0 --strictPort" \
            "/tmp/servicehub_demo_startup.log"
    fi
fi

# Start Sandbox if requested
if [ "$START_SANDBOX" = true ]; then
    if [ ! -d "$SANDBOX_DIR" ]; then
        echo -e "${YELLOW}⚠ Sandbox directory not found at $SANDBOX_DIR${NC}"
    else
        start_service "Sandbox" "5175" "" \
            "cd $SCRIPT_DIR && npm run -w apps/sandbox dev -- --port 5175 --host 0.0.0.0 --strictPort" \
            "/tmp/servicehub_sandbox_startup.log"
    fi
fi

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
echo -e "${GREEN}║   ✓ Services Running Successfully!     ║${NC}"
echo -e "${YELLOW}╚════════════════════════════════════════╝${NC}"
echo ""

if [ "$START_API" = true ]; then
    echo -e "${BLUE}📍 API Endpoints:${NC}"
    echo -e "  • ${GREEN}HTTP:  ${API_HTTP_URL}${NC}"
    if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" != "127.0.0.1" ]; then
        echo -e "  • ${GREEN}Remote: http://${SERVER_IP}:5153${NC}"
    fi
    echo -e "  • ${GREEN}Docs:   ${API_HTTP_URL}/scalar/v1${NC}"
    echo ""
fi

if [ "$START_WEB" = true ]; then
    echo -e "${BLUE}🌐 Web UI:${NC}"
    echo -e "  • ${GREEN}http://localhost:${WEB_PORT}${NC}   ← from this machine"
    if [ -n "$SERVER_IP" ] && [ "$SERVER_IP" != "127.0.0.1" ]; then
        echo -e "  • ${GREEN}http://${SERVER_IP}:${WEB_PORT}${NC}   ← from remote machines (by IP)"
    fi
    if [ -n "$SERVER_HOSTNAME" ] && [ "$SERVER_HOSTNAME" != "localhost" ]; then
        echo -e "  • ${GREEN}http://${SERVER_HOSTNAME}:${WEB_PORT}${NC}   ← from remote machines (by hostname)"
    fi
    echo ""
fi

if [ "$START_DEMO" = true ] && [ -d "$DEMO_DIR" ]; then
    echo -e "${BLUE}📦 Demo (experimental — see apps/demo/README.md):${NC}"
    echo -e "  • ${GREEN}http://localhost:5174${NC}"
    echo ""
fi

if [ "$START_SANDBOX" = true ] && [ -d "$SANDBOX_DIR" ]; then
    echo -e "${BLUE}🏖️  Sandbox (experimental — see apps/sandbox/README.md):${NC}"
    echo -e "  • ${GREEN}http://localhost:5175${NC}"
    echo ""
fi

echo -e "${BLUE}📋 Running Processes:${NC}"
for pid in "${PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
        echo -e "  • ${GREEN}PID: $pid${NC}"
    fi
done
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
