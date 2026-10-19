// 从已下载的文件树里挑出与「收纳格 / 胶囊 / 分组排列」相关的实现文件
import { readFileSync } from 'node:fs';

const raw = JSON.parse(readFileSync('tools/deskbox-tree.json', 'utf8'));
const files = raw.tree.filter(x => x.type === 'blob').map(x => ({ p: x.path, s: x.size || 0 }));

const isSmoke = p => /(Smoke|Test|Tests|\.Designer\.)/i.test(p);
const isCode = p => /\.(xaml|cs)$/i.test(p) && !p.endsWith('.g.cs') && !p.endsWith('.g.i.cs');

const groups = {
  '收纳格/网格': /(Fence|Grid|Tile|Cell|Container|Stash|Drawer|Bin)\b|(Fence|Grid|Tile|Cell|Container|Stash|Drawer)/i,
  '胶囊/收起': /(Capsule|Pill|Collapse|Roll|Fold|Minimiz)/i,
  '分组/排列': /(Group|Arrange|Layout|Snap|Align|Sort)/i,
  '桌面/入口': /(Desktop|Shell|Tray|Hotkey)/i,
};

for (const [name, re] of Object.entries(groups)) {
  const hits = files.filter(f => isCode(f.p) && !isSmoke(f.p) && re.test(f.p))
    .sort((a, b) => b.s - a.s).slice(0, 12);
  console.log(`\n== ${name}（${hits.length}）==`);
  for (const h of hits) console.log(`  ${(h.s / 1024).toFixed(1)}KB  ${h.p}`);
}

console.log('\n== docs/ 里的设计文档（前 25）==');
const docs = files.filter(f => f.p.startsWith('docs/') && f.p.endsWith('.md')).sort((a, b) => b.s - a.s).slice(0, 25);
for (const d of docs) console.log(`  ${(d.s / 1024).toFixed(1)}KB  ${d.p}`);
