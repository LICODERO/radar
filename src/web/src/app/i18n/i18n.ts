import { Injectable, computed, signal } from '@angular/core';
import { EN } from './en';
import { Message, MsgKey, PL } from './pl';

export type Lang = 'pl' | 'en';
export const LANGS: readonly Lang[] = ['pl', 'en'];
export type Params = Record<string, string | number>;

const STORAGE_KEY = 'radar.lang';
const DICTS: Record<Lang, Record<MsgKey, Message>> = { pl: PL, en: EN };

/** Pure translation, usable outside components (scene builder, scan state, validators). */
export function translate(lang: Lang, key: MsgKey, params?: Params): string {
  const msg = DICTS[lang][key];
  let text: string;
  if (typeof msg === 'string') {
    text = msg;
  } else {
    const n = Number(params?.['n'] ?? 0);
    text = msg[new Intl.PluralRules(lang).select(n)] ?? msg.other;
  }
  return params ? text.replace(/\{(\w+)\}/g, (m, name: string) => (name in params ? String(params[name]) : m)) : text;
}

/** First language the browser prefers, limited to the supported ones; Polish is the fallback. */
export function detectLang(preferred: readonly string[]): Lang {
  for (const p of preferred) {
    const l = p.toLowerCase().split('-')[0];
    if (l === 'pl' || l === 'en') return l;
  }
  return 'pl';
}

function initialLang(): Lang {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'pl' || stored === 'en') return stored;
  } catch { /* storage unavailable */ }
  return detectLang(typeof navigator === 'undefined' ? [] : (navigator.languages?.length ? navigator.languages : [navigator.language ?? '']));
}

@Injectable({ providedIn: 'root' })
export class I18n {
  readonly lang = signal<Lang>(initialLang());
  /** BCP 47 tag for Intl/date formatting */
  readonly locale = computed(() => (this.lang() === 'pl' ? 'pl-PL' : 'en-GB'));

  constructor() {
    if (typeof document !== 'undefined') document.documentElement.lang = this.lang();
  }

  /** Reads the language signal, so templates and computeds that call it re-render on a switch. */
  readonly t = (key: MsgKey, params?: Params): string => translate(this.lang(), key, params);

  setLang(lang: Lang): void {
    this.lang.set(lang);
    if (typeof document !== 'undefined') document.documentElement.lang = lang;
    try { localStorage.setItem(STORAGE_KEY, lang); } catch { /* storage unavailable */ }
  }
}
