// 一次性脚本：下载 GPL-3.0 全文到 LICENSE（沙箱内 curl/Invoke-WebRequest 不通，node fetch 可以）
import { writeFileSync } from 'node:fs';

const urls = [
  'https://www.gnu.org/licenses/gpl-3.0.txt',
  'https://raw.githubusercontent.com/spdx/license-list-data/main/text/GPL-3.0-only.txt',
];

for (const u of urls) {
  try {
    const r = await fetch(u, { redirect: 'follow' });
    if (!r.ok) { console.log(`skip ${u} -> HTTP ${r.status}`); continue; }
    const t = await r.text();
    if (!t.includes('GNU GENERAL PUBLIC LICENSE') || t.length < 30000) {
      console.log(`skip ${u} -> 内容不对（${t.length} 字节）`); continue;
    }
    writeFileSync('LICENSE.new', t, 'utf8');
    console.log(`OK ${u} -> LICENSE.new ${t.length} 字节`);
    process.exit(0);
  } catch (e) {
    console.log(`fail ${u}: ${e.message}`);
  }
}
process.exit(1);
