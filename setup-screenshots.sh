#!/bin/bash
#
# ServiceHub README Screenshot Setup Script
# Automates the process of organizing and committing screenshots for GitHub README
#
# Usage: ./setup-screenshots.sh /path/to/your/screenshots
#

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Banner
echo -e "${BLUE}"
cat << "EOF"
╔═══════════════════════════════════════════════════════════╗
║                                                           ║
║   ServiceHub README Screenshot Setup                     ║
║   Automated screenshot organization for GitHub display   ║
║                                                           ║
╚═══════════════════════════════════════════════════════════╝
EOF
echo -e "${NC}"

# Configuration
REPO_ROOT="/Users/debasisghosh/Github/servicehub"
SCREENSHOTS_DIR="$REPO_ROOT/docs/screenshots"
README_SOURCE="$REPO_ROOT/UIREADME.md"
README_TARGET="$REPO_ROOT/README.md"
README_BACKUP="$REPO_ROOT/README-OLD.md"

# Expected screenshot filenames (in order)
declare -a EXPECTED_FILES=(
  "01-problem-empty-state.png"
  "02-quickstart-connection-form.png"
  "03-quickstart-connected-namespace.png"
  "06-feature-message-generator-basic.png"
  "07-feature-message-generator-scenarios.png"
  "08-hero-message-browser-loaded.png"
  "09-feature-send-message-topic.png"
  "10-workflow-topic-subscription-step1.png"
  "11-feature-message-details-properties.png"
  "12-feature-message-details-custom-props.png"
  "13-feature-message-details-body.png"
  "14-feature-ai-findings-badge.png"
  "15-feature-ai-insights-error-cluster.png"
  "16-feature-ai-insights-multiple-patterns.png"
  "17-feature-ai-patterns-popup.png"
  "18-feature-dlq-tab-with-ai.png"
  "19-workflow-dlq-investigation-step1.png"
  "20-workflow-dlq-investigation-step2.png"
  "21-workflow-dlq-replay-step3.png"
  "22-workflow-dlq-replay-step4.png"
  "23-feature-search-functionality.png"
  "24-feature-dlq-multiple-deliveries.png"
)

# Functions
log_success() {
  echo -e "${GREEN}✓${NC} $1"
}

log_error() {
  echo -e "${RED}✗${NC} $1"
}

log_warning() {
  echo -e "${YELLOW}⚠${NC} $1"
}

log_info() {
  echo -e "${BLUE}→${NC} $1"
}

# Check if source directory provided
if [ -z "$1" ]; then
  log_error "No source directory provided"
  echo ""
  echo "Usage: $0 /path/to/your/screenshots"
  echo ""
  echo "Example:"
  echo "  $0 ~/Desktop/servicehub-screenshots"
  echo "  $0 ~/Downloads/screenshots"
  echo ""
  exit 1
fi

SOURCE_DIR="$1"

# Validate source directory exists
if [ ! -d "$SOURCE_DIR" ]; then
  log_error "Source directory not found: $SOURCE_DIR"
  exit 1
fi

log_info "Source directory: $SOURCE_DIR"
echo ""

# Step 1: Check repository
log_info "Step 1/8: Validating repository..."

if [ ! -d "$REPO_ROOT" ]; then
  log_error "Repository not found: $REPO_ROOT"
  exit 1
fi

cd "$REPO_ROOT" || exit 1

if [ ! -d ".git" ]; then
  log_error "Not a git repository: $REPO_ROOT"
  exit 1
fi

log_success "Repository validated"
echo ""

# Step 2: Create screenshots directory
log_info "Step 2/8: Creating screenshots directory..."

mkdir -p "$SCREENSHOTS_DIR"
log_success "Directory created: docs/screenshots/"
echo ""

# Step 3: Count and list source files
log_info "Step 3/8: Analyzing source screenshots..."

PNG_COUNT=$(find "$SOURCE_DIR" -maxdepth 1 -name "*.png" | wc -l | tr -d ' ')

echo "   Found $PNG_COUNT PNG files in source directory"

if [ "$PNG_COUNT" -eq 0 ]; then
  log_error "No PNG files found in $SOURCE_DIR"
  exit 1
fi

if [ "$PNG_COUNT" -lt 24 ]; then
  log_warning "Expected 24 PNG files, found only $PNG_COUNT"
  echo "   Continue anyway? (y/n)"
  read -r response
  if [ "$response" != "y" ]; then
    exit 1
  fi
fi

log_success "Source files validated"
echo ""

# Step 4: Check if files need renaming
log_info "Step 4/8: Checking filename format..."

NEEDS_RENAMING=0

for file in "$SOURCE_DIR"/*.png; do
  filename=$(basename "$file")
  
  # Check if filename matches expected pattern (XX-category-description.png)
  if ! [[ "$filename" =~ ^[0-9]{2}-[a-z]+-[a-z-]+\.png$ ]]; then
    NEEDS_RENAMING=1
    break
  fi
done

if [ $NEEDS_RENAMING -eq 1 ]; then
  log_warning "Screenshots need renaming to match expected format"
  echo ""
  echo "   Expected format: XX-category-description.png"
  echo "   Examples:"
  echo "     01-problem-empty-state.png"
  echo "     08-hero-message-browser-loaded.png"
  echo ""
  echo "   Options:"
  echo "     1) I'll rename them manually (exit script)"
  echo "     2) Show me the mapping (then exit)"
  echo "     3) Continue anyway (files may not display correctly)"
  echo ""
  echo "   Choose (1-3): "
  read -r choice
  
  case $choice in
    1)
      log_info "Please rename files and run script again"
      exit 0
      ;;
    2)
      echo ""
      log_info "Expected filenames (create these 24 files):"
      for expected in "${EXPECTED_FILES[@]}"; do
        echo "     • $expected"
      done
      echo ""
      log_info "See docs/SCREENSHOT_RENAMING_GUIDE.md for detailed mapping"
      exit 0
      ;;
    3)
      log_warning "Continuing with current filenames..."
      ;;
    *)
      log_error "Invalid choice"
      exit 1
      ;;
  esac
else
  log_success "Filenames are correctly formatted"
fi
echo ""

# Step 5: Copy files to screenshots directory
log_info "Step 5/8: Copying screenshots..."

COPIED_COUNT=0
for file in "$SOURCE_DIR"/*.png; do
  filename=$(basename "$file")
  cp "$file" "$SCREENSHOTS_DIR/$filename"
  ((COPIED_COUNT++))
done

log_success "Copied $COPIED_COUNT files to docs/screenshots/"
echo ""

# Step 6: Optimize images (optional)
log_info "Step 6/8: Image optimization (optional)..."

if command -v convert &> /dev/null; then
  echo "   ImageMagick detected. Resize images to max width 1200px? (y/n)"
  read -r response
  
  if [ "$response" = "y" ]; then
    log_info "Resizing images..."
    cd "$SCREENSHOTS_DIR" || exit 1
    
    for img in *.png; do
      convert "$img" -resize 1200x\> "$img"
      echo "     Resized: $img"
    done
    
    log_success "Images resized"
  else
    log_info "Skipping resize"
  fi
else
  log_warning "ImageMagick not installed (optional for optimization)"
  echo "   Install: brew install imagemagick"
fi
echo ""

if command -v pngquant &> /dev/null; then
  echo "   pngquant detected. Compress images? (y/n)"
  read -r response
  
  if [ "$response" = "y" ]; then
    log_info "Compressing images..."
    cd "$SCREENSHOTS_DIR" || exit 1
    
    for img in *.png; do
      pngquant --quality=65-80 --ext .png --force "$img" 2>/dev/null || true
      echo "     Compressed: $img"
    done
    
    log_success "Images compressed"
  else
    log_info "Skipping compression"
  fi
else
  log_warning "pngquant not installed (optional for compression)"
  echo "   Install: brew install pngquant"
fi

cd "$REPO_ROOT" || exit 1
echo ""

# Step 7: Update README
log_info "Step 7/8: Updating README.md..."

if [ -f "$README_TARGET" ]; then
  log_warning "README.md already exists"
  echo "   Backup existing README? (y/n)"
  read -r response
  
  if [ "$response" = "y" ]; then
    mv "$README_TARGET" "$README_BACKUP"
    log_success "Backed up to README-OLD.md"
  else
    log_warning "Existing README will be overwritten"
  fi
fi

if [ ! -f "$README_SOURCE" ]; then
  log_error "UIREADME.md not found: $README_SOURCE"
  exit 1
fi

cp "$README_SOURCE" "$README_TARGET"
log_success "README.md updated"
echo ""

# Step 8: Git operations
log_info "Step 8/8: Git operations..."

# Check git status
if [ -n "$(git status --porcelain docs/screenshots/)" ]; then
  log_info "Staging screenshots..."
  git add docs/screenshots/
  log_success "Screenshots staged"
else
  log_warning "No changes to stage (files already committed?)"
fi

if [ -n "$(git status --porcelain README.md)" ]; then
  log_info "Staging README.md..."
  git add README.md
  log_success "README.md staged"
else
  log_warning "README.md unchanged"
fi

# Show summary
echo ""
log_info "Git Status Summary:"
git status --short | grep -E 'docs/screenshots/|README' || echo "   No changes detected"

echo ""
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
log_success "Setup complete!"
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# Verification
log_info "Verification:"
SCREENSHOT_COUNT=$(ls -1 "$SCREENSHOTS_DIR"/*.png 2>/dev/null | wc -l | tr -d ' ')
echo "   Screenshots in docs/screenshots/: $SCREENSHOT_COUNT"

README_IMAGE_COUNT=$(grep -o 'docs/screenshots/[^)]*\.png' "$README_TARGET" | wc -l | tr -d ' ')
echo "   Image references in README.md: $README_IMAGE_COUNT"

if [ "$SCREENSHOT_COUNT" -eq 24 ] && [ "$README_IMAGE_COUNT" -ge 24 ]; then
  log_success "All checks passed!"
else
  log_warning "Some checks failed. Review files manually."
fi

echo ""

# File sizes
log_info "Screenshot file sizes:"
ls -lh "$SCREENSHOTS_DIR"/*.png | awk '{
  size = $5
  file = $9
  
  # Extract just the filename
  split(file, parts, "/")
  filename = parts[length(parts)]
  
  # Check if size is concerning
  if (index(size, "M") > 0) {
    gsub("M", "", size)
    if (size + 0 > 0.5) {
      print "   ⚠  " filename ": " $5 " (>500KB, consider optimizing)"
    } else {
      print "   ✓  " filename ": " $5
    }
  } else {
    print "   ✓  " filename ": " $5
  }
}'

echo ""

# Next steps
log_info "Next Steps:"
echo ""
echo "   1. Review changes:"
echo "      ${BLUE}git status${NC}"
echo ""
echo "   2. Commit changes:"
echo "      ${BLUE}git commit -m \"docs: Add README with 24 screenshots\"${NC}"
echo ""
echo "   3. Push to GitHub:"
echo "      ${BLUE}git push origin main${NC}"
echo ""
echo "   4. Verify on GitHub:"
echo "      ${BLUE}open https://github.com/debdevops/servicehub${NC}"
echo ""

# Optional: Auto-commit
echo "   Auto-commit and push now? (y/n)"
read -r response

if [ "$response" = "y" ]; then
  log_info "Committing changes..."
  
  git commit -m "docs: Add README with 24 product screenshots

- Add comprehensive visual documentation
- 24 screenshots covering features, workflows, and comparisons
- All images organized in docs/screenshots/
- Screenshot-driven README for visual learners
- Includes setup guides and troubleshooting docs

Covers:
- Hero: ServiceHub message browser in action
- Problem/Solution: Azure Portal vs ServiceHub
- Quick Start: 4-step visual setup guide
- Features: Message browser, DLQ, AI insights (8 features)
- Workflows: DLQ investigation (4-step guide)
- Comparison: Portal vs Service Bus Explorer vs ServiceHub
- Security: Read-only architecture proof

Documentation:
- docs/SCREENSHOT_SETUP_GUIDE.md
- docs/ALTERNATIVE_IMAGE_HOSTING.md
- docs/QUICK_REFERENCE_SCREENSHOTS.md
- docs/SCREENSHOT_FIX_SUMMARY.md"
  
  log_success "Changes committed"
  echo ""
  
  log_info "Pushing to GitHub..."
  git push origin main
  
  log_success "Pushed to GitHub"
  echo ""
  
  log_success "Done! View your README: https://github.com/debdevops/servicehub"
  echo ""
  
  # Open in browser
  echo "   Open in browser? (y/n)"
  read -r response
  
  if [ "$response" = "y" ]; then
    open "https://github.com/debdevops/servicehub"
  fi
else
  log_info "Skipping auto-commit. Run commands manually above."
fi

echo ""
log_success "Setup script complete!"
echo ""
