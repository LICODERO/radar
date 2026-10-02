import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AI_TOOLS, AI_TOOL_ICONS } from '../core/ai-tools';
import { RadarStore } from '../core/radar-store';
import { I18n } from '../i18n/i18n';

type AiState = 'checking' | 'ok' | 'missing' | 'unknown';

/** The AI tools installed on this computer. Picking an installed one chooses whose configuration the scan shows (only Claude Code is scanned so far). */
@Component({
  selector: 'app-ai-tools',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ai-tools.html',
  styleUrl: './ai-tools.scss'
})
export class AiTools {
  protected readonly store = inject(RadarStore);
  protected readonly t = inject(I18n).t;
  protected readonly icons = AI_TOOL_ICONS;
  protected readonly rows = computed(() => {
    const list = this.store.cli();
    const checking = this.store.cliChecking();
    return AI_TOOLS.map((tool) => {
      const status = list?.find((x) => x.tool === tool.id);
      const state: AiState = checking ? 'checking' : !status ? 'unknown' : status.found ? 'ok' : 'missing';
      const sub = state === 'ok' ? (status?.version ? 'v' + status.version : this.t('ai.versionUnknown')) : state === 'missing' ? this.t('ai.notFound') : '';
      const title = state === 'ok' ? this.t('ai.select', { name: tool.name }) : state === 'missing' ? this.t('ai.selectMissing', { name: tool.name }) : (status?.path ?? '');
      return { ...tool, state, sub, title, selected: this.store.aiTool() === tool.id, stateText: this.stateText(state) };
    });
  });

  private stateText(state: AiState): string {
    switch (state) {
      case 'ok': return this.t('ai.state.ok');
      case 'missing': return this.t('ai.state.missing');
      case 'checking': return this.t('ai.state.checking');
      default: return this.t('ai.state.unknown');
    }
  }
}
