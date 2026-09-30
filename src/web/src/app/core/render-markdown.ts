/** Tiny, safe markdown renderer: produces blocks of plain-text segments (no HTML is ever injected). */
export interface Seg {
  t: string;
  /** b = bold, c = inline code */
  k?: 'b' | 'c';
}

export type BlockKind = 'h1' | 'h2' | 'h3' | 'li' | 'ol' | 'p' | 'code' | 'fm' | 'quote' | 'table' | 'hr' | 'blank';

export interface Block {
  kind: BlockKind;
  segs: Seg[];
  indent: number;
}

const INLINE = /(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]*\))/g;

export function parseInline(s: string): Seg[] {
  const out: Seg[] = [];
  let last = 0;
  for (const m of s.matchAll(INLINE)) {
    const i = m.index ?? 0;
    if (i > last) out.push({ t: s.slice(last, i) });
    const tok = m[0];
    if (tok.startsWith('**')) out.push({ t: tok.slice(2, -2), k: 'b' });
    else if (tok.startsWith('`')) out.push({ t: tok.slice(1, -1), k: 'c' });
    else out.push({ t: tok.slice(1, tok.indexOf(']')) }); // link: keep the label only
    last = i + tok.length;
  }
  if (last < s.length) out.push({ t: s.slice(last) });
  return out.length ? out : [{ t: s }];
}

const block = (kind: BlockKind, text: string, indent = 0, raw = false): Block => ({
  kind, indent, segs: raw ? [{ t: text }] : parseInline(text)
});

export function renderBlocks(text: string): Block[] {
  const blocks: Block[] = [];
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  let fm = false;
  let code = false;

  lines.forEach((ln, idx) => {
    const trimmed = ln.trim();

    if (trimmed === '---' && (idx === 0 || fm)) { fm = !fm; blocks.push(block('fm', '---', 0, true)); return; }
    if (fm) { blocks.push(block('fm', ln, 0, true)); return; }

    if (ln.trimStart().startsWith('```')) { code = !code; return; }
    if (code) { blocks.push(block('code', ln, 0, true)); return; }

    if (trimmed === '') { blocks.push(block('blank', ' ', 0, true)); return; }
    if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) { blocks.push(block('hr', '', 0, true)); return; }

    const h = /^(#{1,6})\s+(.*)$/.exec(ln);
    if (h) { blocks.push(block(h[1].length === 1 ? 'h1' : h[1].length === 2 ? 'h2' : 'h3', h[2])); return; }

    if (trimmed.startsWith('|')) {
      if (/^\|[\s:|-]+\|?$/.test(trimmed)) return; // table separator row
      const cells = trimmed.replace(/^\||\|$/g, '').split('|').map((c) => c.trim().replace(/`/g, ''));
      blocks.push(block('table', cells.join('  │  '), 0, true));
      return;
    }

    const bullet = /^(\s*)[-*+]\s+(.*)$/.exec(ln);
    if (bullet) { blocks.push(block('li', bullet[2], Math.floor(bullet[1].length / 2))); return; }

    const num = /^(\s*)(\d+\.)\s+(.*)$/.exec(ln);
    if (num) { blocks.push({ kind: 'ol', indent: Math.floor(num[1].length / 2), segs: [{ t: num[2] + ' ' }, ...parseInline(num[3])] }); return; }

    if (trimmed.startsWith('>')) { blocks.push(block('quote', trimmed.replace(/^>\s?/, ''))); return; }

    blocks.push(block('p', ln));
  });
  return blocks;
}
