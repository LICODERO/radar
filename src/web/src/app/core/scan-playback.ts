/** Total time the overlay aims to spend showing the analysed repos, however fast the scanner really is. */
export const TARGET_MS = 5000;
export const MIN_DELAY_MS = 8;
export const MAX_DELAY_MS = 250;

/** Delay between two shown repos: about TARGET_MS in total, bounded for very small and very large scans. */
export function paceDelay(total: number | null | undefined): number {
  if (!total || total <= 0) return MAX_DELAY_MS;
  return Math.max(MIN_DELAY_MS, Math.min(MAX_DELAY_MS, Math.round(TARGET_MS / total)));
}

type Handler = (name: string, data: any) => void;
type Schedule = (fn: () => void, ms: number) => unknown;
type Cancel = (id: unknown) => void;

/**
 * Buffers scan events and hands them to the UI at a readable pace. Only `repo-scanned` events are delayed;
 * the scanner itself is never slowed down. Everything after a paced event waits for it, so order is kept
 * and `completed` shows up only once all repos have been displayed.
 */
export class ScanPlayback {
  private queue: { name: string; data: any }[] = [];
  private timer: unknown = null;

  constructor(
    private readonly handle: Handler,
    private readonly schedule: Schedule = (fn, ms) => setTimeout(fn, ms),
    private readonly cancel: Cancel = (id) => clearTimeout(id as ReturnType<typeof setTimeout>)
  ) {}

  get pending(): number { return this.queue.length; }

  push(name: string, data: any): void {
    this.queue.push({ name, data });
    if (this.timer === null) this.drain();
  }

  /** Shows everything that is still queued right away (used when the user skips the animation). */
  flush(): void {
    this.stopTimer();
    while (this.queue.length) {
      const e = this.queue.shift()!;
      this.handle(e.name, e.data);
    }
  }

  clear(): void {
    this.stopTimer();
    this.queue = [];
  }

  private drain(): void {
    this.timer = null;
    while (this.queue.length) {
      const e = this.queue.shift()!;
      this.handle(e.name, e.data);
      if (e.name === 'repo-scanned') {   // cool-down even when nothing is waiting yet, so a live stream is paced too
        this.timer = this.schedule(() => this.drain(), paceDelay(e.data?.total));
        return;
      }
    }
  }

  private stopTimer(): void {
    if (this.timer !== null) this.cancel(this.timer);
    this.timer = null;
  }
}
