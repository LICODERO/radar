import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ScanResult } from '../core/models';
import { RadarStore } from '../core/radar-store';
import { SCAN_STEP, TOUR_KEY, TOUR_STEPS } from '../core/tour';
import { ScanUiState } from '../core/scan-state';
import { I18n } from '../i18n/i18n';
import { Tour } from './tour';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient()] });
  TestBed.inject(I18n).setLang('en');
  return TestBed.inject(RadarStore);
}

const running = { status: 'running' } as unknown as ScanUiState;
const result = { repos: [], workflows: [], gaps: [] } as unknown as ScanResult;
const flush = () => TestBed.tick();

beforeEach(() => localStorage.removeItem(TOUR_KEY));

describe('tour flow', () => {
  it('walks forward and back, and the last step finishes it and remembers that', () => {
    const store = setup();
    store.startTour();
    expect(store.tourVisible()).toBe(true);
    expect(store.tourIndex()).toBe(0);
    store.tourBack();
    expect(store.tourIndex()).toBe(0);
    for (let i = 0; i < TOUR_STEPS.length - 1; i++) store.tourNext();
    expect(store.tourIndex()).toBe(TOUR_STEPS.length - 1);
    store.tourNext();
    expect(store.tourOpen()).toBe(false);
    expect(localStorage.getItem(TOUR_KEY)).toBe('done');
  });

  it('skipping ends it at once and is remembered as well', () => {
    const store = setup();
    store.startTour();
    store.tourNext();
    store.finishTour();
    expect(store.tourOpen()).toBe(false);
    expect(localStorage.getItem(TOUR_KEY)).toBe('done');
  });

  it('hides behind the scan overlay and continues with the results once the first scan is over', () => {
    const store = setup();
    store.startTour();
    store.tourIndex.set(SCAN_STEP);
    flush();
    store.scan.set(running);
    flush();
    expect(store.tourVisible()).toBe(false);
    store.result.set(result);
    store.scan.set(null);
    flush();
    expect(store.tourVisible()).toBe(true);
    expect(TOUR_STEPS[store.tourIndex()].id).toBe('orbit');
  });

  it('stays on the scan step when that scan ended without results (cancelled, failed)', () => {
    const store = setup();
    store.startTour();
    store.tourIndex.set(SCAN_STEP);
    flush();
    store.scan.set(running);
    flush();
    store.scan.set(null);
    flush();
    expect(store.tourVisible()).toBe(true);
    expect(store.tourIndex()).toBe(SCAN_STEP);
  });

  it('a scan started from the folder step (changing the path starts one) also moves it on afterwards', () => {
    const store = setup();
    store.startTour();
    store.tourIndex.set(1);
    flush();
    store.scan.set(running);
    flush();
    store.result.set(result);
    store.scan.set(null);
    flush();
    expect(TOUR_STEPS[store.tourIndex()].id).toBe('orbit');
  });
});

describe('Tour component', () => {
  it('shows the step counter, the texts and the buttons', async () => {
    const store = setup();
    store.startTour();
    const fixture = TestBed.createComponent(Tour);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.eyebrow')?.textContent).toBe(`STEP 1 / ${TOUR_STEPS.length}`);
    expect(el.querySelector('.ttl')?.textContent).toBe('Welcome to R.A.D.A.R.');
    expect([...el.querySelectorAll('button')].map((b) => b.textContent?.trim())).toEqual(['SKIP', 'BACK', 'NEXT']);
    expect(el.querySelector<HTMLButtonElement>('button:nth-of-type(2)')?.disabled).toBe(true);
    (el.querySelector('.solid') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.ttl')?.textContent).toBe('Pick a folder');
  });
});
