/** A card shows at most `limit` chips; with more, it shows `shown` of them and a "+N" tile that opens the full list. */
export const CHIPS_SHOWN = 5;
export const CHIPS_LIMIT = 6;

export interface Chips<T> {
  visible: T[];
  /** how many items the "+N" tile stands for; 0 when everything is shown */
  extra: number;
}

export function limitChips<T>(items: readonly T[], shown = CHIPS_SHOWN, limit = CHIPS_LIMIT): Chips<T> {
  return items.length > limit ? { visible: items.slice(0, shown), extra: items.length - shown } : { visible: [...items], extra: 0 };
}
