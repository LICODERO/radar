// Generates mock scan results (same shape as the future scanner output).
// Usage: node scripts/gen-mock.mjs
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'mock');
mkdirSync(outDir, { recursive: true });

const AGENTS = {
  'code-reviewer': ['Przegląd zmian przed merge: styl, błędy logiczne, brakujące testy.', ['Read', 'Grep', 'Bash']],
  'test-writer': ['Generuje testy jednostkowe dla zmienionego kodu.', ['Read', 'Edit', 'Bash']],
  'migration-helper': ['Prowadzi migracje EF Core i sprawdza skrypty SQL.', ['Read', 'Edit', 'Bash']],
  'angular-refactor': ['Refaktoryzuje komponenty Angular do standalone i signals.', ['Read', 'Edit', 'Grep']],
  'api-docs': ['Uzupełnia opisy endpointów i dokumentację Swagger.', ['Read', 'Edit']],
  'pipeline-fixer': ['Diagnozuje i naprawia pipeline YAML w Azure DevOps.', ['Read', 'Edit', 'Bash']]
};
const SKILLS = {
  'ef-migrations': 'Kroki tworzenia i weryfikacji migracji Entity Framework.',
  'xunit-tests': 'Konwencje testów xUnit i szablony mocków.',
  'swagger-docs': 'Zasady opisu endpointów i modeli w Swagger.',
  'hangfire-jobs': 'Wzorce zadań cyklicznych i kolejek Hangfire.',
  'serilog-logging': 'Jednolity format logów strukturalnych.',
  'azure-devops-yaml': 'Szablony pipeline i zmienne środowiskowe.',
  'angular-component': 'Struktura i nazewnictwo komponentów Angular.',
  'rxjs-state': 'Wzorce zarządzania stanem w RxJS.',
  'dashboard-charts': 'Standardy wykresów i kafelków w dashboardach.',
  'jest-tests': 'Konwencje testów jednostkowych frontendu.',
  'excel-export': 'Eksport raportów do Excela.'
};
const AGN = { cr: 'code-reviewer', tw: 'test-writer', mh: 'migration-helper', ar: 'angular-refactor', ad: 'api-docs', pf: 'pipeline-fixer' };
const SKN = { ef: 'ef-migrations', xu: 'xunit-tests', sw: 'swagger-docs', hf: 'hangfire-jobs', se: 'serilog-logging', az: 'azure-devops-yaml', ac: 'angular-component', rx: 'rxjs-state', dc: 'dashboard-charts', jt: 'jest-tests', ex: 'excel-export' };
const A = (s) => s.split(' ').filter(Boolean).map((k) => AGN[k]);
const S = (s) => s.split(' ').filter(Boolean).map((k) => SKN[k]);

// name, sample coverage, has CLAUDE.md, stack, initials, agents, skills
const RAW = [
  ['orders-api', 92, 1, '.NET', 'OA', A('cr tw mh'), S('ef xu sw hf se az')],
  ['orders-front', 81, 1, 'Angular', 'OF', A('cr ar'), S('ac rx dc jt')],
  ['orders-hangfire', 64, 1, '.NET', 'OH', A('cr'), S('hf se')],
  ['reports-api', 77, 1, '.NET', 'RA', A('cr ad'), S('ef xu sw se ex')],
  ['reports-front', 58, 1, 'Angular', 'RF', A('ar'), S('ac dc jt')],
  ['auth-service', 33, 1, '.NET', 'AS', A(''), S('se')],
  ['billing-api', 12, 0, '.NET', 'BA', A(''), S('')],
  ['portal-front', 71, 1, 'Angular', 'PF', A('ar tw'), S('ac rx dc jt')],
  ['notify-worker', 8, 0, '.NET', 'NW', A(''), S('')],
  ['infra-pipelines', 46, 1, 'YAML', 'IP', A('pf'), S('az ef')],
  ['invoices-api', 74, 1, '.NET', 'IA', A('cr tw'), S('ef xu sw')],
  ['invoices-front', 66, 1, 'Angular', 'IF', A('ar'), S('ac jt')],
  ['stock-api', 59, 1, '.NET', 'SA', A('cr'), S('ef xu se')],
  ['stock-front', 48, 1, 'Angular', 'SF', A('ar'), S('ac')],
  ['clients-api', 39, 1, '.NET', 'CA', A(''), S('xu se')],
  ['clients-front', 27, 1, 'Angular', 'CF', A(''), S('jt')],
  ['identity-server', 52, 1, '.NET', 'IS', A('cr'), S('se xu')],
  ['gateway-api', 68, 1, '.NET', 'GA', A('cr'), S('sw se az')],
  ['files-service', 21, 1, '.NET', 'FS', A(''), S('se')],
  ['audit-worker', 6, 0, '.NET', 'AW', A(''), S('')],
  ['admin-front', 44, 1, 'Angular', 'AF', A('ar'), S('ac jt')],
  ['shared-ui', 63, 1, 'Angular', 'SU', A('ar'), S('ac dc')],
  ['db-scripts', 15, 0, 'SQL', 'DS', A(''), S('')],
  ['deploy-templates', 57, 1, 'YAML', 'DT', A('pf'), S('az ef')]
];
const WFS = [
  ['pr-review', [0, 1, 2, 3], ['code-reviewer', 'test-writer'], 'Otwarcie pull requesta', 'Automatyczny przegląd PR i uzupełnienie brakujących testów.'],
  ['db-migration', [0, 9], ['migration-helper', 'test-writer', 'pipeline-fixer'], 'Zmiana w katalogu Migrations', 'Migracja bazy od skryptu po wdrożenie w pipeline.'],
  ['front-refactor', [1, 4, 7], ['angular-refactor', 'test-writer', 'code-reviewer'], 'Ręcznie · komenda /refactor', 'Refaktoryzacja komponentów Angular wraz z testami i przeglądem.'],
  ['api-release', [0, 3, 9], ['api-docs', 'code-reviewer', 'pipeline-fixer'], 'Tag release/*', 'Dokumentacja API, końcowy przegląd i wydanie przez pipeline.'],
  ['security-scan', [3, 10, 17], ['code-reviewer', 'pipeline-fixer'], 'Nocny harmonogram', 'Cykliczny przegląd zależności i konfiguracji pod kątem bezpieczeństwa.'],
  ['docs-sync', [3, 10, 12, 17], ['api-docs', 'code-reviewer'], 'Merge do main', 'Synchronizacja dokumentacji API po zmianach w kontrolerach.'],
  ['hotfix', [0, 2, 9, 23], ['code-reviewer', 'test-writer', 'pipeline-fixer'], 'Ręcznie · komenda /hotfix', 'Szybka poprawka z testem regresji i wdrożeniem awaryjnym.'],
  ['e2e-tests', [1, 7, 20], ['test-writer', 'pipeline-fixer'], 'Deploy na staging', 'Testy end-to-end po wdrożeniu na środowisko staging.'],
  ['commit', [0, 1, 2, 3, 4, 7, 10], [], 'Przed każdym commitem', 'Jak przygotować i opisać commit: jedno zdanie po angielsku, czas przeszły.']
];

const partsOf = (has, ag, sk) => ({ claudeMd: has ? 40 : 0, agents: ag ? 30 : 0, skills: sk ? 30 : 0 });
const scoreOf = (p) => p.claudeMd + p.agents + p.skills;

function build(raw, wfs, sample) {
  const repos = raw.map(([name, , has, stack, initials, ag, sk]) => {
    const parts = partsOf(has, ag.length, sk.length);
    const gaps = [];
    if (!has) gaps.push('no-claude-md');
    if (!ag.length) gaps.push('no-agents');
    if (!sk.length) gaps.push('no-skills');
    return {
      id: name, name, path: name, initials, stack, stacks: [stack],
      claudeMd: { exists: !!has, path: 'CLAUDE.md' },
      agents: ag.map((a) => ({ name: a, description: AGENTS[a][0], tools: AGENTS[a][1], model: null, path: `.claude/agents/${a}.md` })),
      skills: sk.map((s) => ({ name: s, description: SKILLS[s], path: `.claude/skills/${s}/SKILL.md` })),
      coverage: { score: scoreOf(parts), parts },
      gaps
    };
  });
  const workflows = wfs.map(([name, mem, agents, when, description], k) => ({
    id: `W${k + 1}`, name, description, when, agents,
    repos: mem.filter((i) => repos[i]).map((i) => ({ repoId: repos[i].id, path: `.claude/workflows/${name}.md`, linked: (i + k) % 5 !== 0 })),
    issues: []
  }));
  const claudeGaps = repos.filter((r) => !r.claudeMd.exists).map((r) => ({ repoId: r.id, type: 'no-claude-md' }));
  const stacks = [...new Set(repos.map((r) => r.stack))];
  return {
    schemaVersion: 1,
    sample,
    scanRoot: '~/projects',
    scannedAt: '2026-09-29T14:07:00+02:00',
    durationMs: 1840,
    summary: {
      repos: repos.length,
      agents: repos.reduce((a, r) => a + r.agents.length, 0),
      skills: repos.reduce((a, r) => a + r.skills.length, 0),
      workflows: workflows.length,
      gaps: claudeGaps.length,
      stacks,
      avgCoverage: Math.round(repos.reduce((a, r) => a + r.coverage.score, 0) / repos.length)
    },
    repos, workflows, gaps: claudeGaps, warnings: []
  };
}

// 24 repos from the mockup
writeFileSync(join(outDir, 'scan-result.json'), JSON.stringify(build(RAW, WFS, true), null, 2));

// Stress mock: deterministic pseudo-random N repos
function stress(n, wfCount) {
  let seed = 7;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296);
  const pick = (arr) => arr[Math.floor(rnd() * arr.length)];
  const domains = ['orders', 'reports', 'billing', 'stock', 'clients', 'auth', 'files', 'audit', 'portal', 'gateway', 'notify', 'admin', 'search', 'catalog', 'pricing', 'hr', 'crm', 'erp', 'mail', 'docs'];
  const kinds = [['api', '.NET'], ['front', 'Angular'], ['worker', '.NET'], ['service', '.NET'], ['scripts', 'SQL'], ['pipelines', 'YAML']];
  const agKeys = Object.keys(AGENTS), skKeys = Object.keys(SKILLS);
  const raw = [], used = new Set();
  for (let i = 0; raw.length < n; i++) {
    const [k, stack] = pick(kinds);
    const name = `${pick(domains)}-${k}${i > 60 ? '-' + Math.floor(i / 20) : ''}`;
    if (used.has(name)) continue;
    used.add(name);
    const has = rnd() > 0.15 ? 1 : 0;
    const ag = agKeys.filter(() => rnd() > 0.7);
    const sk = skKeys.filter(() => rnd() > 0.75);
    const parts = name.split('-');
    raw.push([name, 0, has, stack, (parts[0][0] + (parts[1] ?? parts[0])[0]).toUpperCase(), ag, sk]);
  }
  const wfs = Array.from({ length: wfCount }, (_, k) => {
    const mem = raw.map((_, i) => i).filter(() => rnd() > 0.93);
    const chain = agKeys.filter(() => rnd() > 0.6);
    return [`workflow-${k + 1}`, mem.length ? mem : [0], chain, 'Ręcznie', `Przykładowy workflow numer ${k + 1}.`];
  });
  return build(raw, wfs, true);
}
writeFileSync(join(outDir, 'scan-result-150.json'), JSON.stringify(stress(150, 20)));
writeFileSync(join(outDir, 'scan-result-400.json'), JSON.stringify(stress(400, 40)));
console.log('mock written to', outDir);
