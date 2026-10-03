import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';
import { CoverageToast } from './coverage-toast';

function show(from: number, to: number) {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  const store = TestBed.inject(RadarStore);
  store.coverageToast.set({ from, to });
  const fixture = TestBed.createComponent(CoverageToast);
  fixture.detectChanges();
  return { store, el: fixture.nativeElement as HTMLElement };
}

describe('CoverageToast', () => {
  it('says how much the coverage rose', () => {
    const { el } = show(42, 50);
    expect(el.querySelector('.txt')?.textContent).toBe('Coverage rose: 42% → 50% (+8 pts)');
    expect(el.querySelector('.toast')?.classList.contains('up')).toBe(true);
  });

  it('says how much it fell', () => {
    const { el } = show(60, 55);
    expect(el.querySelector('.txt')?.textContent).toBe('Coverage fell: 60% → 55% (−5 pts)');
    expect(el.querySelector('.toast')?.classList.contains('down')).toBe(true);
  });

  it('says so when nothing changed', () => {
    const { el } = show(50, 50);
    expect(el.querySelector('.txt')?.textContent).toBe('Coverage unchanged: 50%');
  });

  it('can be dismissed', () => {
    const { store, el } = show(42, 50);
    el.querySelector<HTMLButtonElement>('.x')!.click();
    expect(store.coverageToast()).toBeNull();
  });
});
