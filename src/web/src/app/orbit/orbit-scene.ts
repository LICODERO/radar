import { coverageColor } from '../core/palette';
import { Hover, Pick, RepoInfo, WorkflowInfo } from '../core/models';
import {
  LineGeom, Pt, R1, R2, R3, R4, REPO_START_DEG, WF_START_DEG,
  chooseSpread, lineBetween, polar, ringLayout
} from './geometry';

export interface SceneInput {
  repos: RepoInfo[];
  workflows: WorkflowInfo[];
  selId: string | null;
  pick: Pick | null;
  hover: Hover | null;
  /** repo ids matching the search box; null = no filter */
  matches: ReadonlySet<string> | null;
  tick: number;
}

export interface RepoNode {
  id: string;
  initials: string;
  labels: boolean;
  left: number;
  top: number;
  size: number;
  color: string;
  dashed: boolean;
  glow: string;
  glowPx: number;
  op: number;
  label: string;
}
export interface ElementNode {
  key: string;
  repoId: string;
  name: string;
  left: number;
  top: number;
  hit: number;
  sz: number;
  op: number;
  glowPx: number;
  label: string;
}
export interface WfNode {
  id: string;
  left: number;
  top: number;
  size: number;
  color: string;
  glowPx: number;
  label: string;
}
export interface SceneLine extends LineGeom {
  color: string;
  dashed: boolean;
  op: number;
  delay: number;
  anim: string;
}
export interface PopFile {
  repoId: string;
  path: string;
  kind: string;
  color: string;
}
export interface Popover {
  file: PopFile | null;
  left: number;
  top: number;
  color: string;
  kind: string;
  title: string;
  desc: string;
  rows: { k: string; v: string }[];
}
export interface Tip {
  left: number;
  top: number;
  color: string;
  l1: string;
  l2: string;
}
export interface Scene {
  spokes: LineGeom[];
  lines: SceneLine[];
  slots: { left: number; top: number }[];
  agents: ElementNode[];
  skills: ElementNode[];
  wfs: WfNode[];
  repos: RepoNode[];
  halo: { left: number; top: number } | null;
  rip: { left: number; top: number; anim: string } | null;
  popAnim: string;
  tip: Tip | null;
  pop: Popover | null;
}

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

export function buildScene(inp: SceneInput): Scene {
  const { repos, workflows, pick, hover, matches, tick } = inp;
  const n = repos.length;
  const idx = new Map(repos.map((r, i) => [r.id, i] as const));
  const sel = inp.selId !== null && idx.has(inp.selId) ? (idx.get(inp.selId) as number) : -1; // -1: nothing selected
  const empty = n === 0;

  const rl = ringLayout(n, R1, REPO_START_DEG, 28, 5, 4);
  const wl = ringLayout(inp.workflows.length, R4, WF_START_DEG, 30, 8, 3);
  const detailed = n <= 40; // spokes and empty slots only when the ring is not crowded

  const centers = new Map<string, Pt>();
  const repoPos = repos.map((_, i) => polar(rl.radiusOf(i), rl.angleOf(i)));
  repos.forEach((r, i) => centers.set('r' + r.id, repoPos[i]));

  const isH = (kind: string, repoId: string | undefined, name: string) =>
    !!hover && hover.kind === kind && hover.repoId === repoId && hover.name === name;
  const isP = (kind: string, repoId: string | undefined, name: string) =>
    !!pick && pick.kind === kind && pick.repoId === repoId && pick.name === name;

  // where the current pick is used
  let usedIn: string[] = [];
  if (pick) {
    if (pick.kind === 'a') usedIn = repos.filter((r) => r.agents.some((a) => a.name === pick.name)).map((r) => r.id);
    else if (pick.kind === 's') usedIn = repos.filter((r) => r.skills.some((s) => s.name === pick.name)).map((r) => r.id);
    else usedIn = workflows.find((w) => w.id === pick.name)?.repos.map((x) => x.repoId) ?? [];
  }

  const spokes: LineGeom[] = [];
  const slots: { left: number; top: number }[] = [];
  const agents: ElementNode[] = [];
  const skills: ElementNode[] = [];

  repos.forEach((r, i) => {
    const isSel = i === sel;
    const ang = rl.angleOf(i);
    if (detailed) spokes.push(lineBetween(polar(66, ang), polar(330, ang)));
    const dim = matches && !matches.has(r.id) ? 0.4 : 1;

    const place = (
      items: { name: string }[], kind: 'a' | 's', radius: number, maxSp: number, minSp: number, selSp: number,
      sizes: { sel: number; norm: number; dense: number }, hitSel: number, hitDense: number, hitNorm: number
    ) => {
      const sp = isSel ? chooseSpread(items.length, 360, selSp, minSp) : chooseSpread(items.length, rl.step, maxSp, minSp);
      const dense = !isSel && sp.mode === 'radial';
      items.forEach((it, j) => {
        const k = items.length;
        const q = dense
          ? polar(radius + (j - (k - 1) / 2) * 4, ang)
          : polar(radius, ang + (j - (k - 1) / 2) * (sp.mode === 'arc' ? sp.spacing : 0));
        centers.set(kind + r.id + it.name, q);
        const on = isSel || (pick && pick.kind === kind && pick.name === it.name);
        const hot = isP(kind, r.id, it.name) || isH(kind, r.id, it.name);
        const hit = isSel ? hitSel : dense ? hitDense : hitNorm;
        const node: ElementNode = {
          key: kind + r.id + it.name, repoId: r.id, name: it.name,
          left: q[0] - hit / 2, top: q[1] - hit / 2, hit,
          sz: isSel ? sizes.sel : dense ? sizes.dense : sizes.norm,
          op: (on ? 1 : 0.4) * dim,
          glowPx: hot ? (kind === 'a' ? 18 : 16) : isSel ? (kind === 'a' ? 8 : 6) : 0,
          label: (kind === 'a' ? 'Agent ' : 'Skill ') + it.name + ' · ' + r.name
        };
        (kind === 'a' ? agents : skills).push(node);
      });
    };
    place(r.agents, 'a', R2, 5, 3, 8, { sel: 11, norm: 9, dense: 5 }, 18, 6, 14);
    place(r.skills, 's', R3, 2.3, 1.6, 3.4, { sel: 7, norm: 5, dense: 3 }, 14, 5, 10);

    if (detailed) {
      if (r.agents.length === 0) { const q = polar(R2, ang); slots.push({ left: q[0] - 5.5, top: q[1] - 5.5 }); }
      if (r.skills.length === 0) { const q = polar(R3, ang); slots.push({ left: q[0] - 5.5, top: q[1] - 5.5 }); }
    }
  });

  const selRepo = repos[sel];
  const wfs: WfNode[] = workflows.map((w, i) => {
    const q = polar(wl.radiusOf(i), wl.angleOf(i));
    centers.set('w' + w.id, q);
    const lit = (!empty && w.repos.some((x) => x.repoId === selRepo?.id)) || isP('w', undefined, w.id);
    const hot = isP('w', undefined, w.id) || isH('w', undefined, w.id);
    return {
      id: w.id, left: q[0] - wl.size / 2, top: q[1] - wl.size / 2, size: wl.size,
      color: lit ? '#ffffff' : '#8a90a8', glowPx: hot ? 16 : lit ? 8 : 0, label: 'Workflow ' + w.name
    };
  });

  // relation lines
  const grow = tick % 2 ? 'growB' : 'growA';
  const mk = (p: Pt, q: Pt, color: string, dashed: boolean, op: number, delay: number): SceneLine =>
    ({ ...lineBetween(p, q), color, dashed, op, delay, anim: grow });
  const lines: SceneLine[] = [];
  if (selRepo) {
    const sp = repoPos[sel];
    selRepo.agents.forEach((a, j) => lines.push(mk(sp, centers.get('a' + selRepo.id + a.name)!, '#c6ff3d', false, 0.8, 0.05 + j * 0.06)));
    selRepo.skills.forEach((s, j) => lines.push(mk(sp, centers.get('s' + selRepo.id + s.name)!, '#a99bff', false, 0.65, 0.2 + j * 0.05)));
    workflows.filter((w) => w.repos.some((x) => x.repoId === selRepo.id))
      .forEach((w, j) => lines.push(mk(sp, centers.get('w' + w.id)!, '#e6e9f2', true, 0.6, 0.4 + j * 0.08)));
  }
  if (pick) {
    const from = pick.kind === 'w' ? centers.get('w' + pick.name) : centers.get(pick.kind + pick.repoId + pick.name);
    if (from) {
      usedIn.forEach((rid, k) => {
        if (pick.kind !== 'w' && rid === pick.repoId) return;
        const to = centers.get('r' + rid);
        if (to) lines.push(mk(from, to, '#e6e9f2', true, 0.75, 0.05 + Math.min(k, 30) * 0.05));
      });
    }
  }

  const rel = new Set(usedIn);
  const repoNodes: RepoNode[] = repos.map((r, i) => {
    const isSel = i === sel;
    const hov = isH('r', undefined, r.id);
    const matched = !matches || matches.has(r.id);
    const lit = (!pick || isSel || rel.has(r.id)) && matched;
    return {
      id: r.id, initials: r.initials, labels: rl.labels,
      left: repoPos[i][0] - rl.size / 2, top: repoPos[i][1] - rl.size / 2, size: rl.size,
      color: coverageColor(r.coverage.score), dashed: !r.claudeMd.exists,
      glow: r.claudeMd.exists ? 'rgba(198,255,61,.6)' : 'rgba(255,79,163,.7)',
      glowPx: isSel || hov ? 16 : pick && rel.has(r.id) ? 14 : 3,
      op: lit ? 1 : matched ? 0.3 : 0.15,
      label: r.name + ' · pokrycie ' + r.coverage.score + '%'
    };
  });

  const halo = selRepo ? { left: repoPos[sel][0] - 22, top: repoPos[sel][1] - 22 } : null;
  const rip = selRepo ? { left: repoPos[sel][0] - 14, top: repoPos[sel][1] - 14, anim: tick % 2 ? 'ripB' : 'ripA' } : null;

  // hover tooltip
  let tip: Tip | null = null;
  if (hover) {
    let q: Pt | undefined;
    let l1 = '';
    let l2 = '';
    let color = '#c6ff3d88';
    if (hover.kind === 'r') {
      const r = repos[idx.get(hover.name) ?? -1];
      if (r) {
        q = centers.get('r' + r.id); l1 = r.name; color = coverageColor(r.coverage.score);
        l2 = `${r.stack} · ${r.coverage.score}% · ${r.agents.length} AGENCI · ${r.skills.length} SKILLE`;
      }
    } else if (hover.kind === 'a' || hover.kind === 's') {
      q = centers.get(hover.kind + hover.repoId + hover.name); l1 = hover.name;
      l2 = (hover.kind === 'a' ? 'AGENT · ' : 'SKILL · ') + (repos[idx.get(hover.repoId ?? '') ?? -1]?.name ?? '');
    } else {
      const w = workflows.find((x) => x.id === hover.name);
      if (w) { q = centers.get('w' + w.id); l1 = w.name; l2 = `WORKFLOW · ${w.agents.length} KROKI · ${w.repos.length} REPO`; }
    }
    if (q) tip = { left: clamp(q[0], 120, 600), top: q[1] - 22, color, l1, l2 };
  }

  // popover for the current pick
  let pop: Popover | null = null;
  if (pick) {
    const q = pick.kind === 'w' ? centers.get('w' + pick.name) : centers.get(pick.kind + pick.repoId + pick.name);
    if (q) {
      const inis = usedIn.map((id) => repos[idx.get(id) ?? -1]?.initials).filter(Boolean);
      const usedTxt = `${inis.length} repo · ${inis.slice(0, 10).join(', ')}${inis.length > 10 ? '…' : ''}`;
      const base = { left: q[0] > 400 ? q[0] - 258 : q[0] + 20, top: clamp(q[1] - 40, 30, 470) };
      const repo = repos[idx.get(pick.repoId ?? '') ?? -1];
      if (pick.kind === 'a') {
        const a = repo?.agents.find((x) => x.name === pick.name);
        pop = { ...base, file: a && repo ? { repoId: repo.id, path: a.path, kind: 'AGENT', color: '#c6ff3d' } : null,
          kind: 'AGENT', color: '#c6ff3d', title: pick.name, desc: a?.description ?? '',
          rows: [{ k: 'ŚCIEŻKA', v: `${repo?.path}/${a?.path}` }, { k: 'NARZĘDZIA', v: a?.tools.join(' · ') || '—' }, { k: 'UŻYWANY W', v: usedTxt }] };
      } else if (pick.kind === 's') {
        const s = repo?.skills.find((x) => x.name === pick.name);
        pop = { ...base, file: s && repo ? { repoId: repo.id, path: s.path, kind: 'SKILL', color: '#a99bff' } : null,
          kind: 'SKILL', color: '#a99bff', title: pick.name, desc: s?.description ?? '',
          rows: [{ k: 'ŚCIEŻKA', v: `${repo?.path}/${s?.path}` }, { k: 'UŻYWANY W', v: usedTxt }] };
      } else {
        const w = workflows.find((x) => x.id === pick.name);
        // open the copy from the selected repo when it has one, otherwise the first repo that does
        const ref = w?.repos.find((x) => x.repoId === selRepo?.id) ?? w?.repos[0];
        if (w) pop = { ...base, file: ref ? { repoId: ref.repoId, path: ref.path, kind: 'WORKFLOW ' + w.id, color: '#e6e9f2' } : null,
          kind: 'WORKFLOW ' + w.id, color: '#e6e9f2', title: w.name, desc: w.description,
          rows: [{ k: 'KIEDY', v: w.when || '—' }, { k: 'KROKI', v: w.agents.length ? w.agents.join(' → ') : 'bez agentów (sama procedura)' }, { k: 'REPOZYTORIA', v: usedTxt }] };
      }
    }
  }

  return {
    spokes, lines, slots, agents, skills, wfs, repos: repoNodes, halo, rip,
    popAnim: tick % 2 ? 'popB' : 'popA', tip, pop
  };
}

