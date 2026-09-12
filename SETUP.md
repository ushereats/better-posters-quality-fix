# Setting this up as your own plugin repo — exact steps

## 1. Create the GitHub repo

1. On GitHub: **New repository** → name it (e.g. `better-posters-quality-fix`) → public → don't
   initialize with a README (you're pushing existing content).
2. Push this folder as the initial commit:

```
cd better-posters-quality-fix   # this extracted folder
git init
git add .
git commit -m "Fork: render quality badge locally from real MediaStream data"
git branch -M master
git remote add origin https://github.com/YOUR_GITHUB_USERNAME/YOUR_REPO_NAME.git
git push -u origin master
```

Optional but better practice than a fresh `git init`: fork `neurekadev/better-posters` on
GitHub first (keeps commit history + GPL attribution lineage), clone your fork,
copy these changed files over the top, commit, push. Either works.

## 2. Fix the build against real Jellyfin 12.0 packages

You need the **.NET 10 SDK** installed (Jellyfin 12.0 runs on .NET 10).

```
cd BetterPosters
dotnet restore
dotnet build -c Release
```

Fix whatever the compiler flags — see `CHANGES.md` for the specific API
assumptions I couldn't verify without nuget.org access (`IMediaSourceManager.GetMediaStreams`,
`MediaStream.VideoRangeType`, `InternalItemsQuery.Parent`). Also confirm the
exact current `Jellyfin.Controller`/`Jellyfin.Model` version string:

```
dotnet add package Jellyfin.Controller
```

— whatever version that resolves to is what should be in `BetterPosters.csproj`
(currently set to `12.0.0` as a placeholder).

## 3. Publish and package the plugin zip

```
dotnet publish BetterPosters/BetterPosters.csproj -c Release -o publish
cd publish
zip -r ../better-posters-quality-fix_1.0.0.0.zip BetterPosters.dll SixLabors.*.dll
cd ..
```

Jellyfin expects a flat zip: the plugin DLL and its dependency DLLs sitting
directly in the zip root, no subfolder. Include every DLL the publish output
produced that isn't already part of Jellyfin server itself (the three
SixLabors ones are the new ones this fork needs).

## 4. Get the checksum

```
md5sum better-posters-quality-fix_1.0.0.0.zip
```

Jellyfin plugin manifests use MD5.

## 5. Cut a GitHub Release

1. GitHub repo → **Releases** → **Draft a new release**.
2. Tag: `1.0.0` (matches the `version` field's first three parts in the manifest).
3. Upload `better-posters-quality-fix_1.0.0.0.zip` as a release asset.
4. Publish.
5. Copy the asset's direct download URL — right-click it on the release page,
   copy link. That's your `sourceUrl`.

## 6. Fill in manifest.json

Edit `manifest.json` at your repo root (already scaffolded for you):

- `owner`: your GitHub username
- `imageUrl`: your repo path (or delete this field — Jellyfin doesn't require it)
- `sourceUrl`: the release asset URL from step 5
- `checksum`: the MD5 from step 4
- `timestamp`: current UTC time, ISO 8601 (`date -u +%Y-%m-%dT%H:%M:%SZ`)

Commit and push that change.

## 7. Point Jellyfin at your repo

In Jellyfin:

1. **Dashboard → Plugins → Repositories → Add repository**.
2. URL: `https://raw.githubusercontent.com/YOUR_GITHUB_USERNAME/YOUR_REPO_NAME/master/manifest.json`
3. **Dashboard → Plugins → Catalog** — find **Better Posters (Local Quality Fix)**, install.
4. Restart Jellyfin.
5. It installs alongside the original Better Posters (different GUID) —
   disable/remove the original in **Dashboard → Plugins → My Plugins** so they
   don't both try to claim the same poster.

## 8. Shipping updates later

Bump the `version` in `manifest.json` (four-part, e.g. `1.0.1.0`), repeat
steps 3–6 for the new build, add a new entry to the `versions` array in
`manifest.json` (keep old entries — Jellyfin uses this history for rollback).
Jellyfin's scheduled "Check for plugin updates" task (or Catalog page) will
then offer the new version to anyone who added your repo URL.
