(() => {
  const CFG = window.RADAR_SITE || {};
  const BASE = 'https://github.com/' + (CFG.REPO || 'LICODERO/radar');
  const RAW = CFG.RAW || 'https://raw.githubusercontent.com/' + (CFG.REPO || 'LICODERO/radar') + '/main';
  const LANG_KEY = 'radar.site.lang';
  const CONSENT_KEY = 'radar.site.consent';

  const safe = {
    get: (k) => { try { return localStorage.getItem(k); } catch { return null; } },
    set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* storage unavailable */ } }
  };

  // English texts. The Polish ones are what the HTML ships with: they are read from the page the first time, so there is one copy of each.
  const EN = {
    'nav.features': 'Features', 'nav.download': 'Install',
    'hero.title': 'See how ready your repos are for <em>AI agents</em>.',
    'hero.lead': 'R.A.D.A.R. scans a folder of repositories and shows CLAUDE.md, agents, skills and workflows on one orbit. It points out the gaps, rates the quality of your files and gives you ready commands for Claude Code.',
    'hero.fine': 'Free · open source (MIT) · runs locally', 'hero.tag': 'ORBIT · SCAN OF 10 REPOS',
    'cta.install': 'Install', 'cta.more': 'See what it does',
    'f.kicker': 'SCAN · RATE · FILL', 'f.title': 'You\'ll see what AI knows about your repositories, in one place.',
    'f.sub': 'Point it at a folder. R.A.D.A.R. finds the repositories, checks how they are set up for agents and shows what is worth fixing.',
    'c1.no': 'ORBIT', 'c2.no': 'QUALITY', 'c3.no': 'GAPS', 'c4.no': 'DRAFTS', 'c5.no': 'SECOND BRAIN', 'c6.no': 'FLOWS',
    'c1.t': 'The whole picture on one orbit', 'c1.d': 'R.A.D.A.R. lays out your repositories, agents, skills and workflows on concentric rings. You see coverage and gaps at a glance, and a click lights up the relations.',
    'c2.t': 'Not just whether a file exists', 'c2.d': 'R.A.D.A.R. catches a thin or stale CLAUDE.md, references to files that are gone and an agent without a description. Every file gets a 0–100 score and a concrete hint.',
    'c3.t': 'From a gap to a working command', 'c3.d': 'R.A.D.A.R. generates ready prompts for Claude Code. One click opens a terminal in the repository and starts a session with the right task, only after you confirm.',
    'c4.t': 'Agents, skills and workflows from a description', 'c4.d': 'You describe what you need and R.A.D.A.R. generates a draft of the file. It copies shared agents and skills to the repositories that lack them and keeps private ones out of git.',
    'c5.t': 'Memory outside the repository', 'c5.d': 'R.A.D.A.R. sets up a vault of markdown notes outside your repositories and links it to each project through CLAUDE.local.md. The agent reads it without asking, and git knows nothing about it.',
    'c6.t': 'Who talks to whom', 'c6.d': 'You draw which repositories talk to each other, how they authenticate and where the data flows. R.A.D.A.R. writes it into both repositories, so an agent working in one repo knows to check the other before it changes anything.',
    's.kicker': 'FLOW WIZARD', 's.title': 'You\'ll define how your repositories work together. The agent follows it.',
    's.sub': 'On a board you connect the repositories that talk to each other. R.A.D.A.R. writes that relation into both repositories and adds a pointer to CLAUDE.md, so an agent working in repo A also checks repo B, which depends on it.',
    's.l1': '<b>Draw connections</b> between repos: REST, GraphQL, gRPC, events, database.',
    's.l2': '<b>Describe authentication</b>: API key, OAuth, token. It stores only the <b>names of variables</b>, never secrets.',
    's.l3': '<b>Mark the direction</b> of the data where it makes sense.',
    's.l4': '<b>Rules go to both repos</b> with a short pointer in CLAUDE.md. Public, or private (kept out of git).',
    's.l5': '<b>The agent checks first, then asks</b>, before it changes anything in the other repo.', 's.tag': 'FLOW BOARD',
    'v.kicker': 'PRIVATE AND SHARED', 'v.title': 'You\'ll decide what to share with your team.',
    'v.sub': 'R.A.D.A.R. shows in the list whether each agent, skill, workflow or flow rule file is private or shared. You change it with one click and see a plan of the changes before anything is written.',
    'v1.no': 'JUST FOR YOU', 'v1.t': 'Private',
    'v1.l1': 'The file stays on your computer and never reaches the repository.',
    'v1.l2': 'An entry in .git/info/exclude hides it. That file is not committed, so nobody sees what you keep for yourself.',
    'v1.l3': 'Claude still sees it: it finds agents and skills on its own, and an index in CLAUDE.local.md points it to private workflows.',
    'v1.l4': 'New files are private by default.',
    'v2.no': 'FOR THE WHOLE TEAM', 'v2.t': 'Shared',
    'v2.l1': 'The file lives in the repository, so everyone who pulls gets it: teammates and their agents.',
    'v2.l2': 'Flow rules between repos go into .claude/relations.md and are versioned with the code.',
    'v2.l3': 'R.A.D.A.R. never commits. You commit when you decide the team should have it.',
    'v.note': 'The change works both ways, also for files git already tracks (it removes them from the index, the file stays on disk). When the same name exists as shared and private, R.A.D.A.R. warns you, because Claude sees both.',
    'b.kicker': 'SECOND BRAIN', 'b.title': 'You\'ll set up a project memory the agent will maintain. Outside the repository.',
    'b.sub': 'R.A.D.A.R. sets up a vault of markdown notes outside your repositories and links it to the project through CLAUDE.local.md, with access for the agent without asking. Git knows nothing about it.',
    'b.l1': '<b>Raw sources stay untouched.</b> They go into <i>raw/</i> and the agent never edits or deletes them.',
    'b.l2': '<b>The agent keeps the wiki.</b> Pages in <i>memory/</i> link to each other, and the index in <i>_index.md</i> says what is where.',
    'b.l3': '<b>The structure is the same for every project</b>, so the agent knows where to look and where to save a result.',
    'b.l4': '<b>R.A.D.A.R. sets up the structure and links it to the repo.</b> It does not read the vault content: that is the agent\'s job.',
    'b.credit': 'The layout follows Andrej Karpathy\'s "LLM Wiki" pattern: raw sources, a wiki maintained by the model, and a schema that tells the model how.',
    'b.tree': `<b>second-brain/</b>
├─ _indexMain.md        <span>how to use the vault</span>
└─ <b>shop-api/</b>
   ├─ _index.md         <span>index of the wiki pages</span>
   ├─ raw/              <span>sources, read-only</span>
   ├─ memory/           <span>wiki kept by the agent</span>
   └─ outputs/          <span>saved answers</span>`,
    'd.kicker': 'INSTALL · v<span class="ver"></span>', 'd.title': 'You\'ll install it with one sentence to your agent.',
    'd.sub': 'Paste it into Claude Code or any other agent in a terminal. It downloads the right package, checks the checksum and tells you how to start it.',
    'd.paste': 'PASTE THIS TO YOUR CODING AGENT', 'd.copy': 'COPY', 'd.copied': 'COPIED',
    'd.plugin': 'USE CLAUDE CODE? TWO COMMANDS IN A SESSION',
    'd.pluginnote': 'Adds a plugin with the commands <i>/radar:install</i>, <i>/radar:start</i>, <i>/radar:update</i> and <i>/radar:uninstall</i>. The plugin does not contain the app: <i>/radar:install</i> downloads it the same way as the sentence above.',
    'd.more': 'Installing from source and the details: <a data-link="installmd" href="#">INSTALL.md</a>.',
    'd.oa': 'OPTION A', 'd.ob': 'OPTION B',
    'q.title': 'Frequently asked questions',
    'q1': 'Is it free?', 'q1a': 'Yes. R.A.D.A.R. is free and open source under the MIT licence: use it at home or at work, read the code, change it.',
    'q2': 'Does the app send anything to the internet?', 'q2a': 'No. It runs locally and has no telemetry. The only thing that can leave is a command you run yourself in a terminal through Claude Code. (This website uses analytics only after you agree.)',
    'q3': 'Does it change my repositories?', 'q3a': 'The scan is read-only. It writes only after you have seen the plan and confirmed: a new agent, skill or workflow file, marked blocks in CLAUDE.local.md and CLAUDE.md, and entries in .git/info/exclude. The app never makes commits.',
    'q4': 'Which tools does it work with?', 'q4a': 'It scans the Claude Code setup (CLAUDE.md, agents, skills, workflows) and can open Claude Code sessions. You do not need it for the dashboard itself.',
    'q5': 'Can I read and change the code?', 'q5a': 'Yes, the whole project is on GitHub under the MIT licence. You can read it, run it from source (see INSTALL.md) and send a pull request. Because the app touches your repositories, the safety rules it keeps are written down in SECURITY.md and CONTRIBUTING.md.',
    'q6': 'Where do I report a bug or an idea?', 'q6a': 'In GitHub issues:',
    'ft.issues': 'Report a bug or idea', 'ft.changes': 'Changelog', 'ft.repo': 'Source code on GitHub', 'ft.cookies': 'Cookie settings',
    'ck.text': 'This site may use Google Analytics to see how many people visit it. The R.A.D.A.R. app collects nothing. Do you agree to analytics?', 'ck.ok': 'I agree', 'ck.no': 'No, thanks',
    'alt.orbit': 'The R.A.D.A.R. dashboard: repositories, agents, skills and workflows on an orbit, with a selected repository', 'alt.flow': 'The flow wizard: repositories as cards on a board, connected by lines that show who calls whom',
    'meta.title': 'R.A.D.A.R. – Repo AI Discovery And Review',
    'meta.desc': 'R.A.D.A.R. scans your repositories and shows how well they are set up for AI agents. Local, free, macOS · Windows · Linux.'
  };
  const PL_EXTRA = {
    'd.copied': 'SKOPIOWANO',
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

  // ---- links -------------------------------------------------------------------------------------
  const LINKS = { installmd: `${BASE}/blob/main/INSTALL.md`, sums: `${BASE}/releases/latest/download/SHA256SUMS.txt`, releases: `${BASE}/releases`, issues: `${BASE}/issues`, repo: BASE };
  const wireLinks = () => document.querySelectorAll('[data-link]').forEach((a) => { a.href = LINKS[a.dataset.link]; a.rel = 'noopener'; });
  wireLinks();

  // ---- install commands: copy buttons ----------------------------------------------------
  const CMD = {
    agent: { pl: `Przeczytaj ${RAW}/AGENT_INSTALL.md i zainstaluj mi R.A.D.A.R.: wykonaj kroki, sprawdź sumę kontrolną i powiedz, jak go uruchomić.`,
             en: `Read ${RAW}/AGENT_INSTALL.md and set up R.A.D.A.R. for me: run the steps, verify the checksum, and tell me how to start it.` },
    plugin: { pl: '/plugin marketplace add LICODERO/radar\n/plugin install radar@radar', en: '/plugin marketplace add LICODERO/radar\n/plugin install radar@radar' }
  };
  const fillCommands = (lang) => document.querySelectorAll('[data-cmd]').forEach((el) => { const raw = CMD[el.dataset.cmd][lang]; el.dataset.raw = raw; el.replaceChildren(...raw.split('\n').map((line) => { const ln = document.createElement('span'); ln.className = 'ln'; ln.textContent = line; return ln; })); });
  const copyText = async (text) => {
    try { await navigator.clipboard.writeText(text); return true; } catch { /* fall through */ }
    const t = document.createElement('textarea'); t.value = text; t.style.position = 'fixed'; t.style.opacity = '0'; document.body.appendChild(t); t.select();
    let ok = false; try { ok = document.execCommand('copy'); } catch { /* none */ } t.remove(); return ok;
  };
  document.querySelectorAll('button[data-copy]').forEach((b) => b.addEventListener('click', async () => {
    const pre = document.querySelector(`[data-cmd="${b.dataset.copy}"]`);
    if (!(await copyText(pre.dataset.raw || pre.textContent))) return;
    const lang = document.documentElement.lang;
    b.textContent = DICT[lang]['d.copied'] || 'OK'; b.classList.add('done');
    setTimeout(() => { b.textContent = DICT[document.documentElement.lang]['d.copy']; b.classList.remove('done'); }, 1600);
  }));

  // ---- language ----------------------------------------------------------------------------------
  function apply(lang) {
    const d = DICT[lang];
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach((el) => { const v = d[el.dataset.i18n]; if (v !== undefined) el.textContent = v.replace(/<[^>]+>/g, ''); });
    document.querySelectorAll('[data-i18n-html]').forEach((el) => { const v = d[el.dataset.i18nHtml]; if (v !== undefined) el.innerHTML = v; });
    document.querySelectorAll('img[data-shot]').forEach((img) => { img.src = `assets/img/${img.dataset.shot}-${lang}.webp`; img.alt = d['alt.' + img.dataset.shot]; });
    document.querySelectorAll('.lang button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.lang === lang)));
    document.title = d['meta.title'];
    document.querySelector('meta[name=description]').content = d['meta.desc'];
    fillVersion();
    fillCommands(lang);
    wireLinks();
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
