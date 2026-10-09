# R.A.D.A.R. one-page site

Static HTML, CSS and JS, no build step. Bilingual (PL/EN, switch in the header, choice kept in `localStorage`, default from the browser language, Polish fallback). Fonts are bundled in `assets/fonts` (OFL), nothing is loaded from third parties except Google Analytics, and only after the visitor agrees.

```bash
python3 -m http.server 8077 --directory site     # preview at http://127.0.0.1:8077
```

- `assets/config.js`: `RAW` (where `AGENT_INSTALL.md`, `install.sh` and `install.ps1` are served from; the page builds the paste-to-agent sentence and the install commands from it), `VERSION` (shown in the page; bump with every release), `REPO` (the public releases repo the download links point to), `GA_ID` (leave empty here).
- Download links are `https://github.com/<REPO>/releases/latest/download/<file>`, so they never change between versions as long as the release assets keep their names (`radar-osx-arm64.zip`, `radar-osx-x64.zip`, `radar-win-x64.zip`, `radar-linux-x64.tar.gz`, `SHA256SUMS.txt`).
- Texts: the Polish ones are in `index.html`, the English ones in `assets/site.js` (`EN`), matched by `data-i18n` / `data-i18n-html` keys.
- Screenshots in `assets/img/*.webp` come from the app run on a demo workspace (never real repos), one per language.
- Deploy: `.github/workflows/pages.yml` publishes `site/` with GitHub Pages (custom domain in `site/CNAME`; DNS: a CNAME record `radar` pointing to `licodero.github.io`). Analytics: set the repository variable `GA_ID` (a GA4 id) and the cookie banner and the script switch on by themselves; without it the site has neither.
