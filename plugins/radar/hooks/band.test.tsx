import { expect, test } from 'claude-code/testing'

test('the commands pane draws its tree on every surface', async $ => {
  for (const surface of ['terminal', 'desktop', 'vscode'] as const) {
    const ui = await $.ui.mount({
      plugin: 'radar',
      surface,
      component: 'Pane',
      requestId: 'radar-commands',
      props: { title: 'R.A.D.A.R.', isFocused: false, bodyColumns: 80 },
    })
    expect(await ui.find({ type: 'Button', text: /\/radar status/ })).toBeDefined()
    await ui.unmount()
  }
})
