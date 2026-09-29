import { describe, expect, it } from 'vitest';
import { parseInline, renderBlocks } from './render-markdown';

const kinds = (s: string) => renderBlocks(s).map((b) => b.kind);
const text = (b: { segs: { t: string }[] }) => b.segs.map((s) => s.t).join('');

describe('parseInline', () => {
  it('splits bold and inline code', () => {
    expect(parseInline('a **b** `c` d')).toEqual([{ t: 'a ' }, { t: 'b', k: 'b' }, { t: ' ' }, { t: 'c', k: 'c' }, { t: ' d' }]);
  });

  it('keeps only the label of links', () => {
    expect(parseInline('see [docs](http://x.y) now').map((s) => s.t).join('')).toBe('see docs now');
  });

  it('never returns an empty list', () => {
    expect(parseInline('')).toEqual([{ t: '' }]);
  });
});

describe('renderBlocks', () => {
  it('renders headings, lists, paragraphs and blank lines', () => {
    expect(kinds('# A\n\n## B\n### C\n- x\n  - y\n1. one\ntext')).toEqual(['h1', 'blank', 'h2', 'h3', 'li', 'li', 'ol', 'p']);
  });

  it('marks nested list items with an indent', () => {
    const b = renderBlocks('- x\n  - y\n    - z');
    expect(b.map((x) => x.indent)).toEqual([0, 1, 2]);
  });

  it('treats frontmatter and code fences as raw blocks', () => {
    const b = renderBlocks('---\nname: x **not bold**\n---\n```\n# not a heading\n```\nafter');
    expect(b.map((x) => x.kind)).toEqual(['fm', 'fm', 'fm', 'code', 'p']);
    expect(text(b[1])).toBe('name: x **not bold**');
    expect(text(b[3])).toBe('# not a heading');
  });

  it('does not mistake a later --- for frontmatter and renders it as a rule', () => {
    expect(kinds('# T\n\n---\ntext')).toEqual(['h1', 'blank', 'hr', 'p']);
  });

  it('renders tables row by row and drops the separator', () => {
    const b = renderBlocks('| workflow | when |\n|---|---|\n| `commit` | before commit |');
    expect(b.map((x) => x.kind)).toEqual(['table', 'table']);
    expect(text(b[1])).toBe('commit  │  before commit');
  });

  it('renders quotes and handles CRLF', () => {
    expect(kinds('> note\r\ntext')).toEqual(['quote', 'p']);
  });

  it('never emits HTML: tags stay plain text', () => {
    const b = renderBlocks('<script>alert(1)</script>');
    expect(b[0].kind).toBe('p');
    expect(text(b[0])).toBe('<script>alert(1)</script>');
  });
});
