// 拉 DeskBox 仓库文件树（github.com 直连不通，api.github.com 可通）
import { writeFileSync } from 'node:fs';

const OWNER = 'Tianyu199509';
const REPO = 'DeskBox';
const headers = { 'User-Agent': 'desknest-research', 'Accept': 'application/vnd.github+json' };

async function tryBranch(b) {
  const url = `https://api.github.com/repos/${OWNER}/${REPO}/git/trees/${b}?recursive=1`;
  const r = await fetch(url, { headers });
  if (!r.ok) { console.log(`  branch ${b}: HTTP ${r.status}`); return null; }
  const j = await r.json();
  return { branch: b, tree: j.tree || [], truncated: j.truncated };
}

for (const b of ['main', 'master']) {
  console.log(`尝试分支 ${b} …`);
  const res = await tryBranch(b);
  if (!res || res.tree.length === 0) continue;
  writeFileSync('tools/deskbox-tree.json', JSON.stringify(res, null, 1), 'utf8');
  console.log(`OK 分支=${res.branch} 文件数=${res.tree.length} truncated=${res.truncated}`);

  const files = res.tree.filter(x => x.type === 'blob');
  const byExt = {};
  for (const f of files) {
    const m = f.path.match(/\.([A-Za-z0-9]+)$/);
    const ext = m ? m[1].toLowerCase() : '(none)';
    byExt[ext] = (byExt[ext] || 0) + 1;
  }
  console.log('扩展名分布：', Object.entries(byExt).sort((a, b) => b[1] - a[1]).slice(0, 12));

  const top = {};
  for (const f of files) {
    const seg = f.path.split('/');
    const k = seg.length > 1 ? seg[0] + '/' : '(root)';
    top[k] = (top[k] || 0) + 1;
  }
  console.log('顶层目录：', Object.entries(top).sort((a, b) => b[1] - a[1]).slice(0, 15));

  const kw = /(box|tile|grid|capsule|group|arrange|fence|layout|storage|container)/i;
  const hits = files.filter(f => kw.test(f.path) && /\.(xaml|cs|xaml\.cs|h|cpp|hpp)$/i.test(f.path));
  console.log(`\n关键词命中 ${hits.length} 个，前 30：`);
  for (const h of hits.slice(0, 30)) console.log('  ' + h.path + '  (' + (h.size || 0) + 'B)');
  process.exit(0);
}
console.log('两个分支都失败');
process.exit(1);
