#!/usr/bin/env bash
#
# Stages a publish snapshot of dragon-tor onto the github-main branch.
#
# Runs entirely inside the separate publish worktree, so the Unity project
# folder is never touched. Does NOT commit and does NOT push - it leaves the
# snapshot staged so you can review it, write your own commit message, and
# push when you choose.
#
# Usage:   bash tools/sync-publish.sh [source-branch]
#          (source-branch defaults to dragon-tor)

set -euo pipefail

SOURCE_BRANCH="${1:-dragon-tor}"
PUBLISH_TREE="C:/UnityProjects/StealthDragons-publish"

# Files that live ONLY on the publish branch. dragon-tor must never overwrite
# these: they are what makes the GitHub page a GitHub page.
PRESERVE=(
  .gitattributes
  .gitignore
  README.md
  "Assets/Sprites/StealthDragonsMatch.png"
  "Assets/Sprites/StealthDragonsMatch.png.meta"
  "Assets/Sprites/StealthDragonsTor.png"
  "Assets/Sprites/StealthDragonsTor.png.meta"
  "Assets/Sprites/StealthDragonsVictory.png"
  "Assets/Sprites/StealthDragonsVictory.png.meta"
)

# Dev-only Unity packages stripped from the published manifest and lockfile.
# Removed from the snapshot only - the local project keeps them.
HIDE_PACKAGES=(
  "com.coplaydev.unity-mcp"
)

# Kept out of the public repo. This list must be explicit and complete:
# read-tree stages every path from the source branch regardless of .gitignore,
# because .gitignore only ever applies to untracked files.
EXCLUDE=(
  ".vscode"
  "UserSettings"
  "Assets/Plugins"
  "Assets/Plugins.meta"
  "Assets/ThirdParty"
  "Assets/ThirdParty.meta"
  "Assets/ScriptTemplates"
  "Assets/ScriptTemplates.meta"
)

cd "$PUBLISH_TREE"

branch=$(git rev-parse --abbrev-ref HEAD)
if [ "$branch" != "github-main" ]; then
  echo "ERROR: publish worktree is on '$branch', expected 'github-main'." >&2
  exit 1
fi

echo "Syncing $SOURCE_BRANCH -> github-main (staged only, no commit, no push)"

# 0. Start from a known-clean github-main so re-running is idempotent.
git reset --hard HEAD --quiet
git clean -fdq

# 1. Make index+worktree exactly the source branch. read-tree --reset replaces
#    the index wholesale, which also drops case-variant duplicates
#    (DragonaTOR/ vs Dragonator/, Arrow/Target.png vs arrow/target.png).
git read-tree -u --reset "$SOURCE_BRANCH"

# 2. Put the publish-only files back from the branch tip.
for f in "${PRESERVE[@]}"; do
  if git cat-file -e "HEAD:$f" 2>/dev/null; then
    git checkout HEAD -- "$f"
  else
    echo "  note: '$f' not present on github-main, skipping"
  fi
done

# 3. Re-stage through the clean filters so .gitattributes actually applies -
#    this is what converts binaries to Git LFS pointers and normalises line
#    endings. read-tree writes blobs directly and bypasses all filters.
git add --renormalize .

# 4. Drop the excluded paths. --cached leaves them on disk.
git rm -r --cached -q -f --ignore-unmatch "${EXCLUDE[@]}"

# 5. Strip dev-only packages from the published manifest/lockfile. Edits the
#    JSON by key rather than by line, so it survives reordering. Only the
#    snapshot is affected; the local project keeps the package.
if [ ${#HIDE_PACKAGES[@]} -gt 0 ]; then
  python - "$PUBLISH_TREE" "${HIDE_PACKAGES[@]}" <<'PY'
import io, json, os, sys

tree, hide = sys.argv[1], sys.argv[2:]
for rel in ("Packages/manifest.json", "Packages/packages-lock.json"):
    path = os.path.join(tree, rel)
    if not os.path.exists(path):
        continue
    with io.open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    deps = data.get("dependencies", {})
    removed = [k for k in hide if k in deps]
    for key in removed:
        del deps[key]
    if removed:
        with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(data, fh, indent=2)
            fh.write("\n")
        print("  hid %s from %s" % (", ".join(removed), rel))
PY
  git add Packages/manifest.json Packages/packages-lock.json
fi

# 6. Correct a typo in the published .gitignore: the ScriptTemplates rule reads
#    "/[Aa]ssets/Assets/ScriptTemplates.meta", which matches nothing, so that
#    orphan .meta kept showing up as untracked. Idempotent - a no-op once the
#    corrected line is committed on github-main.
if grep -q '^/\[Aa\]ssets/Assets/ScriptTemplates\.meta$' .gitignore; then
  sed -i 's|^/\[Aa\]ssets/Assets/ScriptTemplates\.meta$|/[Aa]ssets/ScriptTemplates.meta|' .gitignore
  git add .gitignore
  echo "  fixed ScriptTemplates.meta rule in .gitignore"
fi

echo
echo "Staged. Review with:"
echo "  git -C \"$PUBLISH_TREE\" status"
echo "  git -C \"$PUBLISH_TREE\" diff --cached --stat"
echo
echo "Then commit with your own message and push:"
echo "  git -C \"$PUBLISH_TREE\" commit -m \"your description\""
echo "  git -C \"$PUBLISH_TREE\" push github github-main:main"
