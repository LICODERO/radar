import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { I18n } from '../i18n/i18n';
import { VisibilityTag } from './visibility-tag';

function render(v: unknown): HTMLElement {
  TestBed.inject(I18n).setLang('en');
  const fixture = TestBed.createComponent(VisibilityTag);
  fixture.componentRef.setInput('visibility', v);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('VisibilityTag', () => {
  it('labels public, private and untracked items', () => {
    expect(render('public').querySelector('.tag')?.textContent?.trim()).toBe('PUBLIC');
    expect(render('private').querySelector('.tag.private')?.textContent?.trim()).toBe('PRIVATE');
    expect(render('untracked').querySelector('.tag.untracked')?.textContent?.trim()).toBe('UNTRACKED');
  });

  it('explains the state in a tooltip', () => {
    expect(render('private').querySelector('.tag')?.getAttribute('data-tip')).toContain('never reaches a commit');
  });

  it('hangs the tooltip from the right edge by default and from the left edge on request', () => {
    expect(render('public').querySelector('.tag')?.classList.contains('tip-r')).toBe(true);
    TestBed.resetTestingModule();
    TestBed.inject(I18n).setLang('en');
    const fixture = TestBed.createComponent(VisibilityTag);
    fixture.componentRef.setInput('visibility', 'public');
    fixture.componentRef.setInput('side', 'l');
    fixture.detectChanges();
    const tag = (fixture.nativeElement as HTMLElement).querySelector('.tag')!;
    expect(tag.classList.contains('tip-l')).toBe(true);
    expect(tag.classList.contains('tip-r')).toBe(false);
  });

  it('shows nothing when git could not tell or the scan predates visibility', () => {
    expect(render('unknown').querySelector('.tag')).toBeNull();
    expect(render(null).querySelector('.tag')).toBeNull();
    expect(render(undefined).querySelector('.tag')).toBeNull();
  });
});
