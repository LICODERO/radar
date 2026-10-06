import { ConflictItem, NameConflict } from './models';
import { Shell, quote } from './commands';

/** The path to delete for an item: a skill is its whole folder. */
export function removalPath(kind: NameConflict['kind'], item: ConflictItem): string {
  return kind === 'skill' ? item.path.replace(/\/SKILL\.md$/, '') : item.path;
}

/** `cd <repo>` for the shell, so the commands below it work from any folder. */
export function cdCommand(shell: Shell, repoDir: string): string {
  return shell === 'powershell' ? `Set-Location -LiteralPath ${quote(shell, repoDir)}` : `cd ${quote(shell, repoDir)}`;
}

/**
 * The command that deletes one conflicting item: `git rm` for a shared (tracked) one, so the deletion can be committed, and a plain delete
 * for a local one. The app never runs it; the user copies it.
 */
export function removalCommand(shell: Shell, kind: NameConflict['kind'], item: ConflictItem): string {
  const path = removalPath(kind, item);
  const dir = kind === 'skill';
  if (item.visibility === 'public') return `git rm${dir ? ' -r' : ''} -- ${quote(shell, path)}`;
  return shell === 'powershell'
    ? `Remove-Item${dir ? ' -Recurse' : ''} -LiteralPath ${quote(shell, path)}`
    : `rm${dir ? ' -r' : ''} -- ${quote(shell, path)}`;
}

/** The repo's folder as the server's OS writes it. */
export function repoDir(scanRoot: string, repoPath: string, shell: Shell): string {
  const sep = shell === 'powershell' ? '\\' : '/';
  return `${scanRoot.replace(/[\\/]+$/, '')}${sep}${repoPath.replace(/[\\/]/g, sep)}`;
}
