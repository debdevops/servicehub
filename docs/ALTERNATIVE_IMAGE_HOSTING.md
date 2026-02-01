# Alternative Image Hosting: GitHub Issues Method

If direct screenshot embedding in your repository isn't working (large files, .gitignore conflicts, etc.), use this **bulletproof alternative**: Host images via GitHub Issues.

## Why This Works

- GitHub automatically hosts images uploaded to Issues/PRs
- Permanent URLs (`user-images.githubusercontent.com`)
- No repository storage limits
- Works even if files aren't in git
- CDN-backed (fast global delivery)

---

## 📤 Method 1: Manual Upload (Quick Start)

### Step 1: Create or Open Any Issue

1. Go to your repository: `https://github.com/debdevops/servicehub/issues`
2. Click **New Issue** or open any existing issue (even closed ones work)
3. Title: "README Screenshots" (for organization)

### Step 2: Upload Screenshots

1. **In the issue comment box**, drag and drop your 24 PNG files
2. GitHub will upload and generate URLs:
   ```markdown
   ![01-problem-empty-state](https://user-images.githubusercontent.com/123456789/abc123-01-problem.png)
   ![02-connection-form](https://user-images.githubusercontent.com/123456789/def456-02-connection.png)
   ... (22 more)
   ```

3. **Copy all generated markdown** (don't submit issue yet)

### Step 3: Extract URLs

GitHub generates markdown like:
```markdown
![image](https://user-images.githubusercontent.com/123456789/abc123-filename.png)
```

**Copy these URLs to a text file** for the next step.

### Step 4: Update README.md

Replace all local image paths with GitHub-hosted URLs:

**Before (local):**
```markdown
![Hero](docs/screenshots/08-hero-message-browser-loaded.png)
```

**After (GitHub-hosted):**
```markdown
![ServiceHub Message Browser](https://user-images.githubusercontent.com/123456789/abc123-08-hero.png)
```

### Step 5: Commit Updated README

```bash
git add README.md
git commit -m "Update README with GitHub-hosted screenshots"
git push origin main
```

### Step 6: Close Issue (Optional)

You can close the "README Screenshots" issue. URLs remain permanent even after issue closure.

---

## 🤖 Method 2: Automated Upload Script (For 24+ Images)

### Python Script: `upload-screenshots.py`

```python
#!/usr/bin/env python3
"""
Upload screenshots to GitHub Issues and update README with URLs.
Requires: requests, python-dotenv
Install: pip install requests python-dotenv
"""

import os
import re
import sys
from pathlib import Path

import requests
from dotenv import load_dotenv

load_dotenv()

# Configuration
GITHUB_TOKEN = os.getenv("GITHUB_TOKEN")  # Personal access token
REPO_OWNER = "debdevops"
REPO_NAME = "servicehub"
ISSUE_NUMBER = 1  # Create issue #1 first: "README Screenshots"

# Validate token
if not GITHUB_TOKEN:
    print("❌ Error: GITHUB_TOKEN not set in environment")
    print("Create token: https://github.com/settings/tokens/new")
    print("Scopes needed: repo (full access)")
    sys.exit(1)

# GitHub API endpoints
ISSUE_COMMENTS_URL = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/issues/{ISSUE_NUMBER}/comments"
HEADERS = {
    "Authorization": f"token {GITHUB_TOKEN}",
    "Accept": "application/vnd.github.v3+json"
}


def upload_image_to_issue(image_path: Path) -> str:
    """
    Upload image to GitHub issue and return markdown URL.
    
    Note: GitHub doesn't have a direct image upload API.
    This function posts a comment with the image, extracts the URL,
    then optionally deletes the comment.
    """
    print(f"📤 Uploading {image_path.name}...")
    
    # Read image as binary
    with open(image_path, "rb") as f:
        image_data = f.read()
    
    # Create comment with image attachment
    # GitHub's comment endpoint accepts markdown with images
    comment_body = f"![{image_path.stem}]({image_path.name})"
    
    # Post comment (this doesn't actually upload the image via API)
    # Alternative: Use GitHub GraphQL API or manual upload
    
    # For now, return placeholder
    # User must manually drag-drop images into issue
    print(f"⚠️  Manual upload required for {image_path.name}")
    print(f"   Drag into issue: https://github.com/{REPO_OWNER}/{REPO_NAME}/issues/{ISSUE_NUMBER}")
    
    return None


def extract_urls_from_issue(issue_number: int) -> dict:
    """
    Extract image URLs from issue comments.
    Returns: {filename: github_url}
    """
    print(f"🔍 Fetching image URLs from issue #{issue_number}...")
    
    response = requests.get(ISSUE_COMMENTS_URL, headers=HEADERS)
    response.raise_for_status()
    
    comments = response.json()
    image_urls = {}
    
    # Parse markdown images from all comments
    for comment in comments:
        body = comment["body"]
        # Find: ![alt](https://user-images.githubusercontent.com/...)
        matches = re.findall(r'!\[([^\]]*)\]\((https://user-images\.githubusercontent\.com/[^\)]+)\)', body)
        
        for alt_text, url in matches:
            # Try to extract original filename from alt text
            filename = alt_text if alt_text else url.split('/')[-1]
            image_urls[filename] = url
    
    print(f"✅ Found {len(image_urls)} image URLs")
    return image_urls


def update_readme(readme_path: Path, image_urls: dict) -> None:
    """
    Replace local image paths with GitHub-hosted URLs.
    """
    print(f"📝 Updating {readme_path}...")
    
    with open(readme_path, "r") as f:
        content = f.read()
    
    # Replace patterns: ![...](docs/screenshots/FILENAME.png)
    # With: ![...](GITHUB_URL)
    
    updated_content = content
    replacements = 0
    
    for filename, github_url in image_urls.items():
        # Pattern: docs/screenshots/FILENAME.png or docs/screenshots/FILENAME
        pattern = rf'(!\[[^\]]*\])\(docs/screenshots/{re.escape(filename)}[^\)]*\)'
        replacement = rf'\1({github_url})'
        
        updated_content, count = re.subn(pattern, replacement, updated_content)
        replacements += count
    
    # Write updated README
    with open(readme_path, "w") as f:
        f.write(updated_content)
    
    print(f"✅ Updated {replacements} image references")


def main():
    """Main execution flow."""
    print("🚀 GitHub Screenshot Uploader for README")
    print("=" * 50)
    
    # Paths
    repo_root = Path("/Users/debasisghosh/Github/servicehub")
    screenshots_dir = repo_root / "docs" / "screenshots"
    readme_path = repo_root / "README.md"
    
    # Verify directories exist
    if not screenshots_dir.exists():
        print(f"❌ Screenshots directory not found: {screenshots_dir}")
        sys.exit(1)
    
    if not readme_path.exists():
        print(f"❌ README.md not found: {readme_path}")
        sys.exit(1)
    
    # Get all PNG files
    screenshot_files = sorted(screenshots_dir.glob("*.png"))
    print(f"📸 Found {len(screenshot_files)} screenshots")
    
    if len(screenshot_files) == 0:
        print("❌ No PNG files found in docs/screenshots/")
        sys.exit(1)
    
    # Method: Extract URLs from existing issue comments
    print("\n⚠️  MANUAL STEP REQUIRED:")
    print(f"1. Open: https://github.com/{REPO_OWNER}/{REPO_NAME}/issues/{ISSUE_NUMBER}")
    print(f"2. Drag-drop all {len(screenshot_files)} PNG files into comment box")
    print(f"3. Wait for URLs to generate (don't submit yet)")
    print(f"4. Copy all generated markdown")
    print(f"5. Submit comment")
    print(f"6. Press Enter here to continue...")
    input()
    
    # Extract URLs from issue
    image_urls = extract_urls_from_issue(ISSUE_NUMBER)
    
    if len(image_urls) == 0:
        print("❌ No image URLs found in issue comments")
        print("   Make sure you uploaded images and submitted comment")
        sys.exit(1)
    
    # Update README
    update_readme(readme_path, image_urls)
    
    print("\n✅ COMPLETE!")
    print("Next steps:")
    print("  git add README.md")
    print("  git commit -m 'Update README with GitHub-hosted screenshots'")
    print("  git push origin main")


if __name__ == "__main__":
    main()
```

### Usage

```bash
# Install dependencies
pip3 install requests python-dotenv

# Create .env file with GitHub token
echo "GITHUB_TOKEN=your_github_personal_access_token" > .env

# Run script
python3 upload-screenshots.py
```

**GitHub Personal Access Token:**
1. Go to: https://github.com/settings/tokens/new
2. Name: "README Screenshot Uploader"
3. Scopes: `repo` (full access)
4. Generate token
5. Copy to `.env` file

---

## 🎯 Method 3: Bulk Upload with GitHub CLI

### Install GitHub CLI

```bash
# macOS
brew install gh

# Authenticate
gh auth login
```

### Upload Screenshots to Issue

```bash
# Create issue for screenshots
gh issue create \
  --title "README Screenshots" \
  --body "Hosting images for README.md" \
  --repo debdevops/servicehub

# Note: GitHub CLI doesn't support image upload directly
# Still requires manual drag-drop into issue comment
```

---

## 📊 URL Extraction Script

After uploading images to an issue, extract URLs:

```bash
#!/bin/bash
# save as: extract-image-urls.sh

ISSUE_NUMBER=1
REPO="debdevops/servicehub"

echo "🔍 Extracting image URLs from issue #${ISSUE_NUMBER}..."

# Fetch issue comments
gh api repos/$REPO/issues/$ISSUE_NUMBER/comments --jq '.[].body' | \
  grep -o 'https://user-images.githubusercontent.com/[^)]*' | \
  sort | uniq > image-urls.txt

echo "✅ Saved $(wc -l < image-urls.txt) URLs to image-urls.txt"
cat image-urls.txt
```

**Run:**
```bash
chmod +x extract-image-urls.sh
./extract-image-urls.sh
```

**Output:** `image-urls.txt`
```
https://user-images.githubusercontent.com/123456/abc-01-problem.png
https://user-images.githubusercontent.com/123456/def-02-connection.png
... (22 more)
```

---

## 🔄 Automated README Update Script

```bash
#!/bin/bash
# save as: update-readme-with-urls.sh

README="README.md"
URLS_FILE="image-urls.txt"
BACKUP="README.md.backup"

# Backup README
cp "$README" "$BACKUP"
echo "📋 Created backup: $BACKUP"

# Read URLs into array
mapfile -t URLS < "$URLS_FILE"

# Screenshot filename mapping (in order)
FILES=(
  "01-problem-empty-state"
  "02-quickstart-connection-form"
  "03-quickstart-connected-namespace"
  "06-feature-message-generator-basic"
  "07-feature-message-generator-scenarios"
  "08-hero-message-browser-loaded"
  "09-feature-send-message-topic"
  "10-workflow-topic-subscription-step1"
  "11-feature-message-details-properties"
  "12-feature-message-details-custom-props"
  "13-feature-message-details-body"
  "14-feature-ai-findings-badge"
  "15-feature-ai-insights-error-cluster"
  "16-feature-ai-insights-multiple-patterns"
  "17-feature-ai-patterns-popup"
  "18-feature-dlq-tab-with-ai"
  "19-workflow-dlq-investigation-step1"
  "20-workflow-dlq-investigation-step2"
  "21-workflow-dlq-replay-step3"
  "22-workflow-dlq-replay-step4"
  "23-feature-search-functionality"
  "24-feature-dlq-multiple-deliveries"
)

# Replace each local path with GitHub URL
for i in "${!FILES[@]}"; do
  FILE="${FILES[$i]}"
  URL="${URLS[$i]}"
  
  if [ -n "$URL" ]; then
    echo "🔄 Replacing docs/screenshots/${FILE}.png → $URL"
    
    # Use sed to replace (macOS version)
    sed -i '' "s|docs/screenshots/${FILE}\.png|${URL}|g" "$README"
  else
    echo "⚠️  No URL for $FILE"
  fi
done

echo "✅ README.md updated with GitHub-hosted URLs"
echo "📝 Review changes:"
echo "   diff $BACKUP $README"
```

**Run:**
```bash
chmod +x update-readme-with-urls.sh
./update-readme-with-urls.sh
```

**Verify changes:**
```bash
# Show differences
diff README.md.backup README.md

# Check URLs were inserted
grep -o 'https://user-images.githubusercontent.com/[^)]*' README.md | wc -l
# Should output: 24 (or number of screenshots)
```

---

## ✅ Verification

After updating README with GitHub-hosted URLs:

```bash
# Commit changes
git add README.md
git commit -m "Switch to GitHub-hosted screenshots via Issues"
git push origin main

# View on GitHub
open https://github.com/debdevops/servicehub
```

**All images should now display** without needing files in `docs/screenshots/`.

---

## 🎁 Benefits of GitHub Issues Hosting

✅ **No repository bloat** (screenshots don't count toward repo size)  
✅ **CDN delivery** (faster global loading)  
✅ **Permanent URLs** (survive even if issue is deleted)  
✅ **No .gitignore conflicts**  
✅ **Easy updates** (upload new screenshot, get new URL, update README)  
✅ **Works for private repos** (images remain accessible if repo is public README)

---

## 🚨 Important Notes

### URL Permanence

GitHub-hosted image URLs (`user-images.githubusercontent.com`) are permanent as long as:
- The repository exists
- The issue/comment exists (even if closed/locked)
- GitHub continues hosting the image

**Best practice:** Keep the "README Screenshots" issue open and pinned for reference.

### Private Repositories

If your repository is **private**, GitHub-hosted images will only be visible to users with repository access. This is perfect for internal documentation.

To share images publicly from a private repo:
- Make repository public (images become public)
- Or upload to public gist instead

### Image URL Format

GitHub generates URLs like:
```
https://user-images.githubusercontent.com/USER_ID/IMAGE_ID-FILENAME.png
```

- `USER_ID`: Your GitHub user ID (number)
- `IMAGE_ID`: Unique identifier for this upload
- `FILENAME`: Original filename (preserved)

---

## 🆘 Troubleshooting

### Problem: Images Not Generating URLs

**Symptom:** Drag-drop into issue doesn't create markdown URLs.

**Solutions:**
1. **Check file size**: GitHub Issues limit: 25MB per file (PNGs should be <1MB)
2. **Try different browser**: Chrome/Firefox work best
3. **Check file format**: Only PNG, JPG, GIF supported
4. **Rename files**: Remove special characters (use `-` instead of spaces)

### Problem: URLs Not Replacing in README

**Symptom:** Script runs but README unchanged.

**Check:**
```bash
# Verify URLs file has content
cat image-urls.txt | wc -l  # Should be 24

# Verify filenames match
grep -o 'docs/screenshots/[^)]*\.png' README.md | sort | uniq
# Should match FILES array in script
```

**Fix:** Adjust `FILES` array in script to match your actual filenames.

---

## 🎉 Success Checklist

- [ ] Created issue: "README Screenshots"
- [ ] Uploaded all 24 PNG files to issue comment
- [ ] Extracted 24 GitHub-hosted URLs
- [ ] Updated README.md with new URLs
- [ ] Committed and pushed changes
- [ ] Verified images display on GitHub
- [ ] No broken image icons
- [ ] All 24 screenshots visible

**Your README is now bulletproof!** Images will display regardless of local repository state. 🚀

---

## 📚 Additional Resources

- [GitHub Markdown Guide](https://docs.github.com/en/get-started/writing-on-github)
- [GitHub Issues API](https://docs.github.com/en/rest/issues)
- [GitHub CLI Documentation](https://cli.github.com/manual/)
- [GitHub Personal Access Tokens](https://github.com/settings/tokens)

---

**Recommended Approach:**

1. **Try direct repository hosting first** (`docs/screenshots/` method)
2. **If that fails** (file size, .gitignore, corporate firewall), use GitHub Issues hosting
3. **If both fail**, consider external CDN (Imgur, Cloudinary) or self-hosted images

For most projects, **direct repository hosting** is cleanest. GitHub Issues hosting is a great fallback when constraints prevent direct commits.
