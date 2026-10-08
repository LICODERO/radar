// The only file to touch for a release or for analytics. The release workflow can rewrite VERSION before the site is uploaded.
window.RADAR_SITE = {
  VERSION: '0.1.0',
  REPO: 'lookashdev/radar-releases',
  // Google Analytics 4 measurement id (G-XXXXXXXXXX). Empty = no analytics and no cookie banner. It only loads after the visitor accepts.
  GA_ID: ''
};
