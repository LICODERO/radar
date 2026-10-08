(() => {
  const CFG = window.RADAR_SITE || {};
  const BASE = 'https://github.com/' + (CFG.REPO || 'lookashdev/radar-releases');
  const LANG_KEY = 'radar.site.lang';
  const CONSENT_KEY = 'radar.site.consent';

  const safe = {
    get: (k) => { try { return localStorage.getItem(k); } catch { return null; } },
    set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* storage unavailable */ } }
  };

  // English texts. The Polish ones are what the HTML ships with: they are read from the page the first time, so there is one copy of each.
  const EN = {
    'nav.features': 'Features', 'nav.flow': 'Flow', 'nav.safety': 'Safety', 'nav.download': 'Download',
    'hero.title': 'See how ready your repos are for <em>AI agents</em>.',
    'hero.lead': 'R.A.D.A.R. scans a folder of repositories and shows CLAUDE.md, agents, skills and workflows on one orbit. It points out the gaps, rates the quality of your files and gives you ready commands for Claude Code and Codex CLI.',
    'hero.fine': 'Free · runs locally · macOS, Windows, Linux', 'hero.tag': 'ORBIT · SCAN OF 10 REPOS',
    'cta.download': 'Download for', 'cta.more': 'See what it does',
    'strip.1t': 'Local', 'strip.1d': 'Nothing leaves your computer', 'strip.2t': 'Zero network', 'strip.2d': 'No telemetry, accounts or sign-ups',
    'strip.3t': 'After you confirm', 'strip.3d': 'Writes to a repo only with your consent', 'strip.4t': '3 systems',
    'f.kicker': 'SCAN · RATE · FILL', 'f.title': 'Everything AI knows about your repositories, in one place.',
    'f.sub': 'Point it at a folder. R.A.D.A.R. finds the repositories, checks how they are set up for agents and shows what is worth fixing.',
    'c1.no': 'ORBIT', 'c2.no': 'QUALITY', 'c3.no': 'GAPS', 'c4.no': 'DRAFTS', 'c5.no': 'SECOND BRAIN', 'c6.no': 'FLOW',
    'c1.t': 'The whole picture on one orbit', 'c1.d': 'Repositories, agents, skills and workflows on concentric rings. Coverage and gaps are visible at a glance, and a click lights up the relations.',
    'c2.t': 'Not just whether a file exists', 'c2.d': 'A thin or stale CLAUDE.md, references to files that are gone, an agent without a description. Every file gets a 0–100 score and a concrete hint.',
    'c3.t': 'From a gap to a working command', 'c3.d': 'Ready prompts for Claude Code and Codex CLI. One click opens a terminal in the repo, but only after you confirm.',
    'c4.t': 'Agents, skills and workflows from a description', 'c4.d': 'Describe it in plain words and R.A.D.A.R. drafts the file. It copies shared agents between repos and keeps private ones out of git.',
    'c5.t': 'Memory outside the repository', 'c5.d': 'A vault of markdown notes outside your repos, wired to each project through CLAUDE.local.md. The agent reads it without asking, and git sees nothing.',
    'c6.t': 'Who talks to whom', 'c6.d': 'Draw which repo calls which, how it authenticates and where the data flows. An agent working in one repo knows it has to look at the other.',
    's.kicker': 'FLOW WIZARD', 's.title': 'An agent that knows a change in one repo touches another.',
    's.sub': 'When you work on a feature in repo A, the agent should check repo B that depends on it. Now you record that knowledge once, on a board.',
    's.l1': '<b>Draw connections</b> between repos: REST, GraphQL, gRPC, events, database.',
    's.l2': '<b>Describe authentication</b>: API key, OAuth, token. It stores only the <b>names of variables</b>, never secrets.',
    's.l3': '<b>Mark the direction</b> of the data where it makes sense.',
    's.l4': '<b>Rules go to both repos</b> with a short pointer in CLAUDE.md. Public, or private (kept out of git).',
    's.l5': '<b>The agent checks first, then asks</b>, before it changes anything in the other repo.', 's.tag': 'FLOW BOARD',
    'h.kicker': 'THREE STEPS', 'h.title': 'From download to your first scan in a minute.',
    'h.1t': 'Unpack and run', 'h.1d': 'Nothing to install. Run the radar file and a local dashboard opens in your browser.',
    'h.2t': 'Pick a folder', 'h.2d': 'Choose the directory with your repositories. The scan is read-only and takes a few seconds.',
    'h.3t': 'Fill the gaps', 'h.3d': 'Copy a ready command or open a Claude Code session in the repo. Describe the flow between repos.',
    'p.kicker': 'SAFETY', 'p.title': 'A tool that touches your repositories has to be careful.',
    'p.sub': 'So it is built not to do anything you have not seen first.',
    'p.1': '<b>Your computer only.</b> The server listens on 127.0.0.1 only, needs a session token and checks the Host and Origin headers.',
    'p.2': '<b>Zero network while running.</b> No telemetry, accounts or background updates. Fonts ship in the package.',
    'p.3': '<b>Reads only AI and markdown files.</b> Never .env, keys or anything outside the repository.',
    'p.4': '<b>Writes only after you confirm.</b> First a plan with the exact text, then your yes. It never makes commits.',
    'p.5': '<b>The server builds the commands.</b> The browser never sends text to run, only identifiers of ready templates.',
    'p.6': '<b>Secrets stay out of files.</b> Flow rules hold the names of environment variables, never their values.',
    'd.kicker': 'DOWNLOAD · v<span class="ver"></span>', 'd.title': 'Zero dependencies. Unpack and run.',
    'd.sub': 'A single package with everything inside, no .NET or Node to install. You only need Claude Code or Codex CLI if you want R.A.D.A.R. to open sessions for you.',
    'd.arm': 'Apple Silicon (M1 and newer)', 'd.all': 'All versions and release notes',
    'n.mac': 'macOS: first start', 'n.macp': 'The app is not signed with an Apple certificate, so the system blocks the first start. Remove the download flag once, in the unpacked folder:',
    'n.win': 'Windows: SmartScreen warning', 'n.winp': 'The file is not signed, so Windows may show "Windows protected your PC". Choose "More info", then "Run anyway". The console window shows the log; close it to quit.',
    'n.lin': 'Linux', 'n.linp': 'Opening a terminal from the app works on macOS and Windows. On Linux, copy the generated commands.',
    'q.title': 'Frequently asked questions',
    'q1': 'Is it free?', 'q1a': 'Yes. You can use R.A.D.A.R. for free, including at work. The details are in the LICENSE file in the package.',
    'q2': 'Does the app send anything to the internet?', 'q2a': 'No. It runs locally and has no telemetry. The only thing that can leave is a command you run yourself in a terminal through Claude Code or Codex CLI. (This website uses analytics only after you agree.)',
    'q3': 'Does it change my repositories?', 'q3a': 'The scan is read-only. It writes only after you have seen the plan and confirmed: a new agent, skill or workflow file, marked blocks in CLAUDE.local.md and CLAUDE.md, and entries in .git/info/exclude. The app never makes commits.',
    'q4': 'Which tools does it work with?', 'q4a': 'It scans the Claude Code setup (CLAUDE.md, agents, skills, workflows) and can open Claude Code and Codex CLI sessions. You need neither for the dashboard itself.',
    'q5': 'Why is the source not open?', 'q5a': 'For now I share ready packages and an issue tracker. The code is private, but nothing stops me from opening it one day. Trust comes from the safety notes above and from the fact that the app works only locally.',
    'q6': 'Where do I report a bug or an idea?', 'q6a': 'In GitHub issues:',
    'ft.issues': 'Report a bug or idea', 'ft.changes': 'Changelog', 'ft.repo': 'Releases repository', 'ft.cookies': 'Cookie settings',
    'ck.text': 'This site may use Google Analytics to see how many people visit it. The R.A.D.A.R. app collects nothing. Do you agree to analytics?', 'ck.ok': 'I agree', 'ck.no': 'No, thanks',
    'alt.orbit': 'The R.A.D.A.R. dashboard: repositories, agents, skills and workflows on an orbit, with a selected repository', 'alt.flow': 'The flow wizard: repositories as cards on a board, connected by lines that show who calls whom',
    'meta.title': 'R.A.D.A.R. – Repo AI Discovery And Review',
    'meta.desc': 'R.A.D.A.R. scans your repositories and shows how well they are set up for AI agents. Local, free, macOS · Windows · Linux.'
  };
  const PL_EXTRA = {
    'alt.orbit': 'Dashboard R.A.D.A.R.: repozytoria, agenci, skille i workflow na orbicie, z zaznaczonym repozytorium',
    'alt.flow': 'Kreator przepływu: repozytoria jako karty na planszy, połączone liniami pokazującymi, kto kogo wywołuje',
    'meta.title': 'R.A.D.A.R. – Repo AI Discovery And Review',
    'meta.desc': 'R.A.D.A.R. skanuje Twoje repozytoria i pokazuje, jak dobrze są przygotowane pod agentów AI. Lokalnie, za darmo, macOS · Windows · Linux.'
  };

  // Polish = the markup as shipped
  const PL = { ...PL_EXTRA };
  document.querySelectorAll('[data-i18n],[data-i18n-html]').forEach((el) => { PL[el.dataset.i18n || el.dataset.i18nHtml] = el.innerHTML; });
  const DICT = { pl: PL, en: EN };

  const ver = CFG.VERSION || '';
  const fillVersion = () => document.querySelectorAll('.ver').forEach((e) => (e.textContent = ver));

  // ---- OS ----------------------------------------------------------------------------------------
  const platform = ((navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || navigator.userAgent || '').toLowerCase();
  const OS = /mac/.test(platform) ? 'mac' : /win/.test(platform) ? 'win' : /linux|x11|cros/.test(platform) ? 'linux' : '';
  const OS_NAME = { mac: 'macOS', win: 'Windows', linux: 'Linux' };

  // ---- links -------------------------------------------------------------------------------------
  document.querySelectorAll('a.os[data-file]').forEach((a) => { a.href = `${BASE}/releases/latest/download/${a.dataset.file}`; if (a.dataset.os === OS) a.classList.add('rec'); });
  const LINKS = { sums: `${BASE}/releases/latest/download/SHA256SUMS.txt`, releases: `${BASE}/releases`, issues: `${BASE}/issues`, repo: BASE };
  document.querySelectorAll('[data-link]').forEach((a) => { a.href = LINKS[a.dataset.link]; a.rel = 'noopener'; });
  const cta = document.getElementById('cta-download');
  if (OS === 'win' || OS === 'linux') cta.href = `${BASE}/releases/latest/download/${OS === 'win' ? 'radar-win-x64.zip' : 'radar-linux-x64.tar.gz'}`;
  document.getElementById('cta-os').textContent = OS ? OS_NAME[OS] : '';

  // ---- language ----------------------------------------------------------------------------------
  function apply(lang) {
    const d = DICT[lang];
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach((el) => { const v = d[el.dataset.i18n]; if (v !== undefined) el.textContent = v.replace(/<[^>]+>/g, ''); });
    document.querySelectorAll('[data-i18n-html]').forEach((el) => { const v = d[el.dataset.i18nHtml]; if (v !== undefined) el.innerHTML = v; });
    document.querySelectorAll('img[data-shot]').forEach((img) => { img.src = `assets/img/${img.dataset.shot}-${lang}.webp`; img.alt = d['alt.' + img.dataset.shot]; });
    document.querySelectorAll('.os.rec').forEach((a) => a.setAttribute('data-rec', lang === 'pl' ? 'POLECANE' : 'RECOMMENDED'));
    document.querySelectorAll('.lang button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.lang === lang)));
    document.title = d['meta.title'];
    document.querySelector('meta[name=description]').content = d['meta.desc'];
    fillVersion();
    safe.set(LANG_KEY, lang);
  }
  document.querySelectorAll('.lang button').forEach((b) => b.addEventListener('click', () => apply(b.dataset.lang)));
  const saved = safe.get(LANG_KEY);
  apply(saved === 'en' || saved === 'pl' ? saved : /^en/i.test(navigator.language || '') ? 'en' : 'pl');

  // ---- reveal on scroll --------------------------------------------------------------------------
  const rv = document.querySelectorAll('.rv');
  if ('IntersectionObserver' in window) {
    const io = new IntersectionObserver((es) => es.forEach((e) => { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } }), { threshold: 0.12 });
    rv.forEach((el, i) => { el.style.transitionDelay = (i % 3) * 70 + 'ms'; io.observe(el); });
  } else rv.forEach((el) => el.classList.add('in'));

  // ---- analytics: only after the visitor agrees --------------------------------------------------
  const GA = CFG.GA_ID;
  if (GA) {
    const box = document.getElementById('consent');
    const settings = document.getElementById('cookie-settings');
    settings.hidden = false;
    const load = () => {
      if (window.__gaLoaded) return;
      window.__gaLoaded = true;
      const s = document.createElement('script');
      s.async = true; s.src = 'https://www.googletagmanager.com/gtag/js?id=' + encodeURIComponent(GA);
      document.head.appendChild(s);
      window.dataLayer = window.dataLayer || [];
      window.gtag = function () { window.dataLayer.push(arguments); };
      window.gtag('js', new Date());
      window.gtag('config', GA, { anonymize_ip: true });
    };
    const choose = (yes) => { safe.set(CONSENT_KEY, yes ? 'yes' : 'no'); box.hidden = true; if (yes) load(); };
    document.getElementById('consent-ok').addEventListener('click', () => choose(true));
    document.getElementById('consent-no').addEventListener('click', () => choose(false));
    settings.addEventListener('click', () => (box.hidden = false));
    const c = safe.get(CONSENT_KEY);
    if (c === 'yes') load(); else if (c !== 'no') box.hidden = false;
  }
})();
