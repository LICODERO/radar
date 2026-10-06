/** A card shows at most three chips per category; with more, it shows three and a "+N" tile that opens the full list, so the card never needs to scroll. */
export const CHIPS_SHOWN = 3;
export const CHIPS_LIMIT = 3;

export interface Chips<T> {
  visible: T[];
  /** how many items the "+N" tile stands for; 0 when everything is shown */
  extra: number;
}

export function limitChips<T>(items: readonly T[], shown = CHIPS_SHOWN, limit = CHIPS_LIMIT): Chips<T> {
  return items.length > limit ? { visible: items.slice(0, shown), extra: items.length - shown } : { visible: [...items], extra: 0 };
}
