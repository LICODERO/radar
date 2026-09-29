/** Helpers for the agent file the user reviews before saving (mirrors the server rules; the server has the last word). */
const NAME_RE = /^[a-z][a-z0-9-]{1,63}$/;

export interface ParsedAgent {
  hasFrontmatter: boolean;
  name: string;
  description: string;
  tools: string;
  body: string;
}

const scalar = (line: string) => line.slice(line.indexOf(':') + 1).trim().replace(/^["']|["']$/g, '');

export function parseAgent(content: string): ParsedAgent {
  const lines = content.replace(/\r\n/g, '\n').split('\n');
  const out: ParsedAgent = { hasFrontmatter: false, name: '', description: '', tools: '', body: '' };
  if (lines[0]?.trim() !== '---') return out;
  const end = lines.findIndex((l, i) => i > 0 && l.trim() === '---');
  if (end < 0) return out;
  out.hasFrontmatter = true;
  for (const l of lines.slice(1, end)) {
    const key = /^([A-Za-z_-]+):/.exec(l)?.[1]?.toLowerCase();
    if (key === 'name') out.name = scalar(l);
    else if (key === 'description') out.description = scalar(l);
    else if (key === 'tools') out.tools = scalar(l);
  }
  out.body = lines.slice(end + 1).join('\n');
  return out;
}

export function isValidAgentName(name: string): boolean {
  return NAME_RE.test(name);
}

export function validateAgent(content: string): string[] {
  const errors: string[] = [];
  const a = parseAgent(content);
  if (!a.hasFrontmatter) return ['Brak poprawnego frontmattera: plik ma zaczynać się od bloku --- ... ---.'];
  if (!isValidAgentName(a.name)) errors.push('Pole name: małe litery, cyfry i myślniki (np. migration-reviewer), 2–64 znaki.');
  if (a.description.length < 10) errors.push('Pole description jest wymagane (min. 10 znaków): kiedy używać agenta.');
  else if (a.description.length > 1024) errors.push('Pole description jest za długie (max 1024 znaki).');
  if (a.body.trim().length < 20) errors.push('Brak treści instrukcji po frontmatterze.');
  return errors;
}

/** Sets (or inserts) the name field of the frontmatter, leaving the rest of the file untouched. */
export function setAgentName(content: string, name: string): string {
  const lines = content.replace(/\r\n/g, '\n').split('\n');
  if (lines[0]?.trim() !== '---') return `---\nname: ${name}\n---\n${content}`;
  const end = lines.findIndex((l, i) => i > 0 && l.trim() === '---');
  const limit = end < 0 ? lines.length : end;
  const at = lines.findIndex((l, i) => i > 0 && i < limit && /^name:/i.test(l));
  if (at >= 0) lines[at] = `name: ${name}`;
  else lines.splice(1, 0, `name: ${name}`);
  return lines.join('\n');
}
