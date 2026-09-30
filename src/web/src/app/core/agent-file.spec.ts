import { describe, expect, it } from 'vitest';
import { translate } from '../i18n/i18n';
import { isValidAgentName, parseAgent, setAgentName, validateAgent } from './agent-file';

const t = (key: Parameters<typeof translate>[1]) => translate('pl', key);
const GOOD = '---\nname: migration-reviewer\ndescription: Use when reviewing EF Core migrations.\ntools: Read, Grep\n---\n\nYou review migrations carefully and report risks.\n';

describe('parseAgent', () => {
  it('reads the frontmatter fields and the body', () => {
    const a = parseAgent(GOOD);
    expect(a.hasFrontmatter).toBe(true);
    expect(a.name).toBe('migration-reviewer');
    expect(a.description).toBe('Use when reviewing EF Core migrations.');
    expect(a.tools).toBe('Read, Grep');
    expect(a.body).toContain('You review migrations');
  });

  it('strips quotes and copes with CRLF', () => {
    expect(parseAgent('---\r\nname: "x-agent"\r\n---\r\nbody').name).toBe('x-agent');
  });

  it('reports a missing or unterminated frontmatter', () => {
    expect(parseAgent('# just text').hasFrontmatter).toBe(false);
    expect(parseAgent('---\nname: x\nno end').hasFrontmatter).toBe(false);
  });
});

describe('validateAgent', () => {
  it('accepts a good file', () => {
    expect(validateAgent(GOOD, t)).toEqual([]);
  });

  it('flags every problem it can see', () => {
    expect(validateAgent('text', t)).toHaveLength(1);
    expect(validateAgent('---\nname: Bad Name\ndescription: short\n---\nx', t).length).toBe(3);
  });
});

describe('isValidAgentName', () => {
  it.each(['code-reviewer', 'a1', 'x-y-z'])('accepts %s', (n) => expect(isValidAgentName(n)).toBe(true));
  it.each(['', 'a', 'Bad', '../x', 'with space', 'under_score', '-lead', 'a'.repeat(65)])('rejects %j', (n) => expect(isValidAgentName(n)).toBe(false));
});

describe('setAgentName', () => {
  it('replaces the name and keeps everything else', () => {
    const r = setAgentName(GOOD, 'new-name');
    expect(parseAgent(r).name).toBe('new-name');
    expect(r).toContain('description: Use when reviewing EF Core migrations.');
    expect(r.endsWith(GOOD.slice(GOOD.indexOf('You review')))).toBe(true);
  });

  it('inserts a name when missing and creates a frontmatter when there is none', () => {
    expect(parseAgent(setAgentName('---\ndescription: d\n---\nbody', 'n-x')).name).toBe('n-x');
    expect(parseAgent(setAgentName('body only', 'n-x')).name).toBe('n-x');
  });

  it('does not touch a name-like line in the body', () => {
    const r = setAgentName('---\ndescription: d\n---\nname: not this one\n', 'n-x');
    expect(r).toContain('name: not this one');
    expect(parseAgent(r).name).toBe('n-x');
  });
});
