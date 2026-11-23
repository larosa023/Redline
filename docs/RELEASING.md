# Releasing

Claude handles releases end to end. For reference:

1. CI must be green on `main`.
2. Run the **Release** workflow (Actions → Release → Run workflow) with a version such as `1.0.0` or `0.9.0-beta.2`.
   Versions with a `-` are published as pre-releases; installed betas update to newer betas, stable installs only see stable releases.
3. The workflow tests, publishes the app and the helper (self-contained, win-x64), packs with Velopack and uploads to GitHub Releases.
4. Installed copies pick the update up within 6 hours (or on next launch) and update with one click.
   If the helper's version changed, the app asks for one admin prompt to update it.
