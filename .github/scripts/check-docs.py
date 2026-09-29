#!/usr/bin/env python3
"""Documentation guard: every link, image and repo path a tracked Markdown file cites must exist.

Why: the docs used to describe 4.0.0 paths, screens and tests that no longer existed, and nobody noticed. This fails the build when a
tracked .md/llms.txt file points at something that is not in the repository.

Checks, per tracked file (archive/ and CHANGELOG.md — a historical record — are skipped):
  * relative links and images  [text](path) · <img src="path"> · <a href="path">
  * absolute github.com/<repo>/blob/main/<path> links
  * `backticked/repo/paths` (not in docs/adr/, which records history) under the top-level folders this repository has (apps/, services/, tests/, scripts/, docs/, ...)
  * in-file and cross-file #anchors are NOT checked (GitHub's slug rules are not worth re-implementing)

Usage: check-docs.py [root]        (defaults to the repository root)
"""
import glob
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..'))
os.chdir(ROOT)

# Paths that legitimately point outside this repository (a sibling repo's file named in prose).
ALLOWED = {'docs/decisions/ADR-004-InfrastructureAttestedDlqObserver.md'}
SKIP_FILES = {'CHANGELOG.md'}
REPO_DIRS = ('apps', 'services', 'tests', 'scripts', 'docs', 'deploy', '.github', 'archive', 'azuredevops')

tracked = subprocess.check_output(['git', 'ls-files', '*.md', 'llms.txt'], text=True).split('\n')
files = [f for f in tracked if f and not f.startswith('archive/') and f not in SKIP_FILES and os.path.exists(f)]

problems = []


def exists(path: str) -> bool:
    return bool(glob.glob(path)) if any(c in path for c in '*{') else os.path.exists(path)


for f in files:
    text = open(f, encoding='utf8').read()
    text = re.sub(r'```.*?```', '', text, flags=re.S)  # fenced code is illustrative, not a promise
    targets = re.findall(r'\]\(\s*<?([^)\s>]+)', text) + re.findall(r'(?:src|href)="([^"]+)"', text)
    for t in targets:
        m = re.match(r'https://github\.com/[^/]+/[^/]+/blob/main/(.+)', t)
        if m:
            path = m.group(1).split('#')[0]
        elif re.match(r'(https?:|mailto:|#|data:)', t):
            continue
        else:
            p = t.split('#')[0].split('?')[0]
            if not p:
                continue
            path = p.lstrip('/') if p.startswith('/') else os.path.normpath(os.path.join(os.path.dirname(f), p))
        if path not in ALLOWED and not exists(path):
            problems.append(f'{f}: link to {t} — {path} does not exist')
    if f.startswith('docs/adr/'):
        continue  # a decision record names the paths of its day, including ones that no longer exist — that is the record
    for c in re.findall(r'`((?:%s)/[A-Za-z0-9_./*{}-]+)`' % '|'.join(REPO_DIRS), text):
        c = c.rstrip('/.')
        if c not in ALLOWED and not exists(c):
            problems.append(f'{f}: cites `{c}` — it does not exist')

if problems:
    print(f'❌ {len(problems)} documentation reference(s) point at something that is not in the repository:\n')
    print('\n'.join(sorted(set(problems))))
    print('\nFix the reference, or move the document out of the tracked set (docs-private/ is git-ignored).')
    sys.exit(1)
print(f'✅ {len(files)} Markdown files checked — every link, image and cited path exists.')
