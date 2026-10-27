# Publish v1.4.0 manually

Run in the SteamMonitorTray repository after reviewing the diff:

```powershell
git diff --stat
git add .
git commit -m "feat: improve profile controls and safe version switching"
git push origin main
```

Create a GitHub Release targeting the pushed main commit:

- Tag: `V1.4.0` (new tag; do not replace older releases).
- Title: `v1.4.0 — Profile controls and version switching`
- Notes: paste the complete contents of `docs/RELEASE_1.4.0.md`.
- Asset: `dist/SteamMonitorTray-1.4.0-portable.zip`.
- Publish as a normal release after checking the uploaded filename and commit.

The ZIP is deliberately ignored by Git: upload it as a release asset. Do not upload a private runtime environment, settings backup, monitor configuration or monitoring history.
