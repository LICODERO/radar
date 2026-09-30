import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { EN } from './en';
import { I18n, detectLang, translate } from './i18n';
import { Message, PL } from './pl';

const texts = (m: Message): string[] => (typeof m === 'string' ? [m] : Object.values(m));
const placeholders = (m: Message): string[] => [...new Set(texts(m).flatMap((t) => t.match(/\{\w+\}/g) ?? []))].sort();

describe('dictionaries', () => {
  it('English has exactly the keys Polish has', () => {
    expect(Object.keys(EN).sort()).toEqual(Object.keys(PL).sort());
  });

  it('every message uses the same placeholders in both languages', () => {
    for (const key of Object.keys(PL) as (keyof typeof PL)[]) {
      expect(placeholders(EN[key]), key).toEqual(placeholders(PL[key]));
    }
  });
});

describe('translate', () => {
  it('fills placeholders and leaves unknown ones alone', () => {
    expect(translate('en', 'header.lastScan', { when: '21:24' })).toBe('LAST SCAN 21:24');
    expect(translate('pl', 'header.lastScan')).toBe('OSTATNI SKAN {when}');
  });

  it('picks the Polish plural category', () => {
    const notes = (n: number) => translate('pl', 'right.notes', { n });
    expect([notes(1), notes(2), notes(5), notes(22), notes(12)]).toEqual(['1 notatka', '2 notatki', '5 notatek', '22 notatki', '12 notatek']);
  });

  it('picks the English plural category', () => {
    expect(translate('en', 'right.agentsCount', { n: 1 })).toBe('1 agent');
    expect(translate('en', 'right.agentsCount', { n: 3 })).toBe('3 agents');
  });
});

describe('detectLang', () => {
  it('uses the first supported language the browser prefers', () => {
    expect(detectLang(['de-DE', 'en-US', 'pl'])).toBe('en');
    expect(detectLang(['pl-PL'])).toBe('pl');
  });

  it('falls back to Polish', () => {
    expect(detectLang(['de-DE'])).toBe('pl');
    expect(detectLang([])).toBe('pl');
  });
});

describe('I18n service', () => {
  beforeEach(() => { localStorage.clear(); TestBed.resetTestingModule(); });

  it('switches the language, updates <html lang> and remembers the choice', () => {
    const i18n = TestBed.inject(I18n);
    i18n.setLang('pl');
    expect(i18n.t('header.scan')).toBe('SKANUJ');
    i18n.setLang('en');
    expect(i18n.t('header.scan')).toBe('SCAN');
    expect(document.documentElement.lang).toBe('en');
    expect(localStorage.getItem('radar.lang')).toBe('en');
  });

  it('starts with the stored language', () => {
    localStorage.setItem('radar.lang', 'pl');
    expect(TestBed.inject(I18n).lang()).toBe('pl');
  });
});
