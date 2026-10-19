// 按路径清单从 raw.githubusercontent 取 DeskBox 源码到 _ref/deskbox/（本地参考，已 gitignore）
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

const OWNER = 'Tianyu199509';
const REPO = 'DeskBox';
const BRANCH = 'main';

const want = [
  'docs/requirements/widget-group-navigation-ux.md',
  'src/DeskBox/Services/WidgetCapsuleArrangementCalculator.cs',
  'src/DeskBox/Services/WidgetCollapseMenuBuilder.cs',
  'src/DeskBox/Controls/WidgetGroupTitleSwitcher.Tabs.cs',
  'src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml',
];

for (const p of want) {
  const url = `https://raw.githubusercontent.com/${OWNER}/${REPO}/${BRANCH}/${p}`;
  try {
    const r = await fetch(url, { headers: { 'User-Agent': 'desknest-research' } });
    if (!r.ok) { console.log(`FAIL ${p} -> HTTP ${r.status}`); continue; }
    const t = await r.text();
    const out = join('_ref', 'deskbox', p);
    mkdirSync(dirname(out), { recursive: true });
    writeFileSync(out, t, 'utf8');
    console.log(`OK   ${p}  ${(t.length / 1024).toFixed(1)}KB`);
  } catch (e) {
    console.log(`ERR  ${p}: ${e.message}`);
  }
}
