// The only file to touch for a release or for analytics. The release workflow can rewrite VERSION before the site is uploaded.
window.RADAR_SITE = {
  VERSION: '0.1.0',
  REPO: 'lookashdev/radar',
  // Where AGENT_INSTALL.md, install.sh and install.ps1 live (a folder URL without the trailing slash). Change it if they move to another repo.
  RAW: 'https://raw.githubusercontent.com/lookashdev/radar/main',
  // Google Analytics 4 measurement id (G-XXXXXXXXXX). Empty = no analytics and no cookie banner. It only loads after the visitor accepts.
  GA_ID: ''
};
