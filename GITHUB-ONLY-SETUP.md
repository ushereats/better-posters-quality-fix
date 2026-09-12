# GitHub-only setup — no terminal, no local build

Everything below happens on github.com in your browser. GitHub's own servers
compile the plugin for you via the included Actions workflow
(`.github/workflows/release.yml`).

## 1. Create the repo

github.com → **New repository** → name it (e.g. `better-posters-quality-fix`)
→ Public → **Create repository**.

## 2. Upload this folder's contents

On your new repo's page → **uploading an existing file** (or drag-and-drop).
Drag in everything from the extracted `fork_repo` folder — all of it,
including the hidden `.github` folder (if your file browser hides
dot-folders, show hidden files first, or use GitHub's "Add file → Upload
files" and drag the whole folder onto the browser window, which usually
preserves the structure).

Commit directly to `master`.

**If `.github/workflows/release.yml` doesn't show up after upload:**
some browsers/drag-drop skip dot-folders. Fix: on GitHub, go to
**Add file → Create new file**, type the path
`.github/workflows/release.yml` in the filename box (GitHub auto-creates the
folders), paste the file's content in, commit.

## 3. Trigger the build — push a tag

The workflow only runs when you push a **tag**, not on every file edit. On
GitHub's website:

1. Go to your repo → **Releases** (right sidebar) → **Create a new release**.
2. Click **Choose a tag** → type `1.0.0` → **Create new tag: 1.0.0 on publish**.
3. Fill in a release title, click **Publish release**.

Publishing a release with a new tag triggers the tag push, which triggers
the workflow. (If it doesn't fire: repo → **Actions** tab → check if the
workflow shows up and ran; GitHub sometimes needs Actions enabled once under
**Settings → Actions → General → Allow all actions**.)

## 4. Watch it build

Repo → **Actions** tab → click the running workflow → watch the steps. Takes
2-4 minutes. If it fails, click the red step to see the error, paste it back
to me.

If it succeeds, go back to the release you created in step 3 — it now has
`plugin.zip` attached, uploaded automatically by the workflow.

## 5. Get the checksum

Still in the **Actions** tab, open the completed run → click the **Publish**
job → find the **Print checksum for manifest.json** step → copy the MD5
value it printed.

## 6. Edit manifest.json — on the website

Repo → click `manifest.json` → pencil icon (Edit) → replace the placeholders:

- `"owner"` → your GitHub username
- `"sourceUrl"` → go to your release page, right-click `plugin.zip`, copy
  link address, paste it here
- `"checksum"` → the MD5 from step 5
- `"timestamp"` → current UTC time in this format: `2026-09-12T14:30:00Z`
  (just type today's date/time close enough, doesn't need to be exact to
  the second)

Commit directly to `master`.

## 7. Point Jellyfin at it

In Jellyfin: **Dashboard → Plugins → Repositories → Add Repository**:

```
https://raw.githubusercontent.com/YOUR_USERNAME/YOUR_REPO_NAME/master/manifest.json
```

**Dashboard → Plugins → Catalog** → find "Better Posters (Local Quality
Fix)" → **Install** → restart Jellyfin.

## 8. Turn it on

**Dashboard → Plugins → My Plugins** → open the fork's settings → enable
Quality Tags. Disable/remove the original Better Posters plugin so they
don't both claim the same poster.

## Shipping an update later

Edit source files on GitHub's website (or re-upload changed ones) → bump the
version number in `manifest.json`'s `versions` array (new entry, keep the
old one) → create a new Release with a new tag (e.g. `1.0.1`) → repeat steps
4-6 for that new tag.
