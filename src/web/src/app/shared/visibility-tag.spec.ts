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

  it('shows nothing when git could not tell or the scan predates visibility', () => {
    expect(render('unknown').querySelector('.tag')).toBeNull();
    expect(render(null).querySelector('.tag')).toBeNull();
    expect(render(undefined).querySelector('.tag')).toBeNull();
  });
});
