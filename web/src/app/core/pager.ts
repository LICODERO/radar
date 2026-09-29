export interface PagerView {
  label: string;
  page: number;
  pages: number[];
  prevDisabled: boolean;
  nextDisabled: boolean;
  from: number;
  to: number;
}

export function pageCount(total: number, size: number): number {
  return Math.max(1, Math.ceil(total / size));
}

export function clampPage(page: number, total: number, size: number): number {
  return Math.max(0, Math.min(pageCount(total, size) - 1, page));
}

/** Pager state as shown under the lists ("1–10 z 24"). Pages are 0-based. */
export function buildPager(total: number, size: number, page: number): PagerView {
  const p = clampPage(page, total, size);
  const n = pageCount(total, size);
  const from = total === 0 ? 0 : p * size + 1;
  const to = Math.min(total, (p + 1) * size);
  return {
    label: `${from}–${to} z ${total}`,
    page: p,
    pages: Array.from({ length: n }, (_, i) => i),
    prevDisabled: p === 0,
    nextDisabled: p === n - 1,
    from,
    to
  };
}
