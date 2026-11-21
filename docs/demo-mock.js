/* Gold Mine · 曜蚀阁 — GitHub Pages 纯静态交互体验模拟层 (Zero Backend Required) */
(() => {
  'use strict';

  const ROOT = 'D:\\Gold Mine';
  const VAULT = 'D:\\Gold Mine\\.vault';
  const OUTER_CAT = 'D:\\Gold Mine\\目录.md';
  const INNER_CAT = 'D:\\Gold Mine\\.vault\\目录_里.md';
  const IGNORE_FILE = 'D:\\Gold Mine\\.ignore';

  let categories = ['角色与设定', '文本与产出', '工具与插件'];
  let subDirs = [
    {
      key: 'outer:角色与设定/星晷机关设定集',
      name: '星晷机关设定集',
      category: '角色与设定',
      zone: 'outer',
      fullPath: ROOT + '\\角色与设定\\星晷机关设定集',
      note: '子目录智能平铺分组演示',
      itemCount: 2,
    },
  ];

  const docBodies = {
    [ROOT + '\\角色与设定\\星晷机关设定集\\守夜钟表匠·艾琳.md']: [
      '# 守夜钟表匠 · 艾琳 (Irene)',
      '',
      '> 「白昼归于秩序与索引，长夜归于齿轮与私藏。」',
      '',
      '## 角色定位',
      '- **身份**：日曜钟表塔第七任守夜人、古籍与精密机芯修复师',
      '- **性格特征**：温和克制、条理分明，厌恶杂乱无章的堆砌，习惯为每一份手稿编撰干净的索书号。',
      '',
      '## 设定亮点（在线展卷演示）',
      '1. 点击上方 **「编辑」** 即可在浏览器内存中试改本文稿并按 `Ctrl + S` 保存；',
      '2. 点击卡片上的 **「复制口令」**，体验一键生成发给桌面 AI 的零污染精准读取指令；',
      '3. 点击右上角天色切换按钮，体验从 **表书架（象牙旧纸）** 切入 **里书架（曜石鎏金）** 的日蚀过渡。',
    ].join('\n'),
    [ROOT + '\\角色与设定\\星晷机关设定集\\日曜塔世界观纲要.md']: [
      '# 日曜塔世界观纲要',
      '',
      '整座钟表塔分为上下两层物理隔绝的藏书空间：',
      '',
      '- **表层书廊（Outer Archive）**：存放公开手稿、日常工具与通用设定，向访客与自动化书记官开放索引。',
      '- **黑曜内殿（Inner Sanctum）**：由 `.ignore` 结界守护的隐秘书库，任何文件名冠以 `_` 前缀的残卷都会在落盘瞬间被自动收容入内殿。',
    ].join('\n'),
    [ROOT + '\\文本与产出\\曜蚀阁设计手记_v4.md']: [
      '# Gold Mine · 曜蚀阁 v4 设计手记',
      '',
      '## 为什么做「人机双栖」本地书架？',
      '- **给自己看**：散落在硬盘各处的 `.md` 角色卡、世界观文稿、解压工具与外部大型软件，双击即可沉浸展卷或一键启动。',
      '- **给桌面 AI 看**：自动维护一份只含「分类 + 名称 + 路径 + 简短备注」的净网书单 `目录.md`，避免 AI 全盘盲扫烧光 Token 或误触私人草稿。',
      '',
      '## 纯本地单文件架构',
      '- 后端基于 Windows 原生内置 C# 编译为百余 KB 的绿色单文件 `曜蚀阁.exe`，零第三方运行时依赖，仅监听 `127.0.0.1`。',
      '- 前端采用零外链、零框架原生实现，内置五代可换装皮肤。',
    ].join('\n'),
    [VAULT + '\\角色与设定\\黑曜秘仪·未公开人物残卷.md']: [
      '# 黑曜秘仪 · 未公开人物残卷（里书架演示）',
      '',
      '> 本卷存放于 `.vault` 隐藏内殿中，外部全局搜索工具（`rg` / `grep`）受 `.ignore` 护盾阻隔默认不可见。',
      '',
      '- 支持在网页上一键将任意藏品 **移回表书架** 或 **收容入里书架**（内置同名防覆盖自动编号保护）。',
      '- 支持 `_文件名.md` 下划线前缀暗号自动剥离前缀并静默归入里书架。',
    ].join('\n'),
    [VAULT + '\\文本与产出\\深夜灵感速记.md']: [
      '# 深夜灵感速记',
      '',
      '这是一份存放在 **🌙 里书架（Inner Sanctum）** 的私密草稿示例。',
      '',
      '当你从表书架切换到里书架时，界面由暖调象牙旧纸平滑过渡为墨黑古金配色，提醒当前正处于私密阅览空间。',
    ].join('\n'),
  };

  let items = [
    {
      id: 'demo-1',
      key: 'outer:角色与设定\\星晷机关设定集\\守夜钟表匠·艾琳.md',
      name: '守夜钟表匠·艾琳.md',
      category: '角色与设定',
      subDir: '星晷机关设定集',
      zone: 'outer',
      isExternal: false,
      isDirectory: false,
      existsOnDisk: true,
      fullPath: ROOT + '\\角色与设定\\星晷机关设定集\\守夜钟表匠·艾琳.md',
      relPath: '角色与设定\\星晷机关设定集\\守夜钟表匠·艾琳.md',
      itemType: 'text',
      ext: '.md',
      size: 1280,
      updatedAt: '2026-11-22 01:30:00',
      note: '主角设定卡 · 点击「展卷」可体验沉浸式古籍阅读器与在线编辑',
      tags: ['角色卡', '蒸汽钟表', '示例'],
      entry: '',
      detectedEntries: [],
      childCount: 0,
    },
    {
      id: 'demo-2',
      key: 'outer:角色与设定\\星晷机关设定集\\日曜塔世界观纲要.md',
      name: '日曜塔世界观纲要.md',
      category: '角色与设定',
      subDir: '星晷机关设定集',
      zone: 'outer',
      isExternal: false,
      isDirectory: false,
      existsOnDisk: true,
      fullPath: ROOT + '\\角色与设定\\星晷机关设定集\\日曜塔世界观纲要.md',
      relPath: '角色与设定\\星晷机关设定集\\日曜塔世界观纲要.md',
      itemType: 'text',
      ext: '.md',
      size: 860,
      updatedAt: '2026-11-22 01:28:00',
      note: '双层书库空间设定与下划线自动收容规则说明',
      tags: ['世界观', '设定集'],
      entry: '',
      detectedEntries: [],
      childCount: 0,
    },
    {
      id: 'demo-3',
      key: 'outer:文本与产出\\曜蚀阁设计手记_v4.md',
      name: '曜蚀阁设计手记_v4.md',
      category: '文本与产出',
      subDir: '',
      zone: 'outer',
      isExternal: false,
      isDirectory: false,
      existsOnDisk: true,
      fullPath: ROOT + '\\文本与产出\\曜蚀阁设计手记_v4.md',
      relPath: '文本与产出\\曜蚀阁设计手记_v4.md',
      itemType: 'text',
      ext: '.md',
      size: 1540,
      updatedAt: '2026-11-22 01:40:00',
      note: '人机双栖索引架构与纯本地零联网设计说明',
      tags: ['架构手记', 'Markdown'],
      entry: '',
      detectedEntries: [],
      childCount: 0,
    },
    {
      id: 'demo-4',
      key: 'outer:工具与插件\\脚本封包工具箱',
      name: '脚本封包工具箱',
      category: '工具与插件',
      subDir: '',
      zone: 'outer',
      isExternal: false,
      isDirectory: true,
      existsOnDisk: true,
      fullPath: ROOT + '\\工具与插件\\脚本封包工具箱',
      relPath: '工具与插件\\脚本封包工具箱',
      itemType: 'bundle',
      ext: '',
      size: 0,
      updatedAt: '2026-11-21 22:15:00',
      note: '多文件工程目录自动聚合为叠纸「工具箱」卡片，可绑定主程序一键启动',
      tags: ['工具箱', '自动化'],
      entry: 'launcher.cmd',
      detectedEntries: ['launcher.cmd', 'unpack.py'],
      childCount: 6,
    },
    {
      id: 'demo-5',
      key: 'ext:1',
      name: 'SillyTavern 本地酒馆工作台',
      category: '工具与插件',
      subDir: '',
      zone: 'outer',
      isExternal: true,
      isDirectory: true,
      existsOnDisk: true,
      fullPath: 'E:\\AI_Apps\\SillyTavern',
      relPath: 'E:\\AI_Apps\\SillyTavern',
      itemType: 'bundle',
      ext: '',
      size: 0,
      updatedAt: '2026-11-21 20:00:00',
      note: '「原位挂牌」演示：几十 GB 的外部大型目录只登记账本，绝不搬移原文件',
      tags: ['原位挂牌', '外部目录'],
      entry: 'Start.bat',
      detectedEntries: ['Start.bat'],
      childCount: 18,
    },
    {
      id: 'demo-6',
      key: 'inner:角色与设定\\黑曜秘仪·未公开人物残卷.md',
      name: '黑曜秘仪·未公开人物残卷.md',
      category: '角色与设定',
      subDir: '',
      zone: 'inner',
      isExternal: false,
      isDirectory: false,
      existsOnDisk: true,
      fullPath: VAULT + '\\角色与设定\\黑曜秘仪·未公开人物残卷.md',
      relPath: '.vault\\角色与设定\\黑曜秘仪·未公开人物残卷.md',
      itemType: 'text',
      ext: '.md',
      size: 980,
      updatedAt: '2026-11-22 01:45:00',
      note: '里书架私密藏品演示 · 物理隔离于隐藏 .vault 目录并受 .ignore 保护',
      tags: ['里书架', '私密残卷'],
      entry: '',
      detectedEntries: [],
      childCount: 0,
    },
    {
      id: 'demo-7',
      key: 'inner:文本与产出\\深夜灵感速记.md',
      name: '深夜灵感速记.md',
      category: '文本与产出',
      subDir: '',
      zone: 'inner',
      isExternal: false,
      isDirectory: false,
      existsOnDisk: true,
      fullPath: VAULT + '\\文本与产出\\深夜灵感速记.md',
      relPath: '.vault\\文本与产出\\深夜灵感速记.md',
      itemType: 'text',
      ext: '.md',
      size: 620,
      updatedAt: '2026-11-22 02:00:00',
      note: '通过 _深夜灵感速记.md 下划线前缀自动剥离并静默收容的草稿示例',
      tags: ['灵感', '自动收容'],
      entry: '',
      detectedEntries: [],
      childCount: 0,
    },
  ];

  function buildCatalogMarkdown(targetZone) {
    const isInner = targetZone === 'inner';
    const lines = [
      isInner ? '# 🌑 Gold Mine 曜蚀阁 · 里书架目录' : '# 🏛️ Gold Mine 曜蚀阁 · 总目录 (表书架)',
      '',
      '> **🤖 致 AI 助手的读取与归档指南（在线演示预览）**',
      `> 本书单由曜蚀阁自动维护，仅记录「分类 + 名称 + 路径 + 备注」，零正文污染、省 Token 额度。`,
      '',
    ];
    for (const c of categories) {
      const list = items.filter((x) => x.category === c && x.zone === targetZone);
      lines.push(`## 📂 ${c} (${list.length})`, '');
      if (!list.length) {
        lines.push('*(当前分类暂无藏品)*', '');
        continue;
      }
      lines.push('| 藏品名称 | 类型 | 路径 | 入口 | 备注说明 |');
      lines.push('| :--- | :--- | :--- | :--- | :--- |');
      for (const it of list) {
        const kind = it.isExternal ? '🔗 外部挂牌' : it.isDirectory ? '🧰 工具箱' : '📝 文稿设定';
        lines.push(`| **${it.name}** | ${kind} | \`${it.fullPath}\` | ${it.entry || '-'} | ${it.note || '-'} |`);
      }
      lines.push('');
    }
    return lines.join('\n');
  }

  function buildState() {
    const catDtos = categories.map((name) => {
      const outerCount = items.filter((x) => x.category === name && x.zone === 'outer').length;
      const innerCount = items.filter((x) => x.category === name && x.zone === 'inner').length;
      return { name, outerCount, innerCount, totalCount: outerCount + innerCount };
    });
    const subDtos = subDirs.map((s) => ({
      ...s,
      itemCount: items.filter((x) => x.category === s.category && x.zone === s.zone && x.subDir === s.name).length,
    }));
    return {
      ok: true,
      rootDir: ROOT,
      vaultDir: VAULT,
      outerCatalogPath: OUTER_CAT,
      innerCatalogPath: INNER_CAT,
      ignoreFilePath: IGNORE_FILE,
      categories: catDtos,
      subDirs: subDtos,
      items: items.slice(),
      logs: ['[在线演示模式] 当前运行于 GitHub Pages 纯前端交互沙盒，所有修改仅保存在当前浏览器页面内存中。'],
    };
  }

  function jsonResp(obj) {
    return Promise.resolve(
      new Response(JSON.stringify(obj), {
        status: 200,
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
      })
    );
  }

  const origFetch = window.fetch.bind(window);
  window.fetch = async function (input, init) {
    const rawUrl = typeof input === 'string' ? input : input?.url || '';
    let u;
    try {
      u = new URL(rawUrl, window.location.origin);
    } catch {
      return origFetch(input, init);
    }

    if (!u.pathname.includes('/api/')) {
      return origFetch(input, init);
    }

    const apiPath = '/api/' + u.pathname.split('/api/')[1];
    let body = {};
    if (init?.body) {
      try {
        body = JSON.parse(init.body);
      } catch {
        body = {};
      }
    }

    if (apiPath === '/api/state') {
      return jsonResp(buildState());
    }

    if (apiPath === '/api/item/content') {
      const p = u.searchParams.get('path') || '';
      if (p === OUTER_CAT) return jsonResp({ ok: true, content: buildCatalogMarkdown('outer') });
      if (p === INNER_CAT) return jsonResp({ ok: true, content: buildCatalogMarkdown('inner') });
      return jsonResp({ ok: true, content: docBodies[p] ?? '# 示例文档\n\n（在线交互演示内容）' });
    }

    if (apiPath === '/api/item/save-content') {
      if (body.path) docBodies[body.path] = String(body.content ?? '');
      return jsonResp({ ok: true, message: '已在演示内存中保存修改' });
    }

    if (apiPath === '/api/item/toggle-zone') {
      const it = items.find((x) => x.key === body.key);
      if (!it) return jsonResp({ ok: false, error: '未找到该藏品' });
      it.zone = it.zone === 'outer' ? 'inner' : 'outer';
      const newKey = `${it.zone}:${it.category}\\${it.name}`;
      const oldPath = it.fullPath;
      it.key = newKey;
      if (!it.isExternal) {
        it.subDir = '';
        it.fullPath = (it.zone === 'inner' ? VAULT : ROOT) + '\\' + it.category + '\\' + it.name;
        if (docBodies[oldPath] !== undefined) {
          docBodies[it.fullPath] = docBodies[oldPath];
        }
      }
      return jsonResp({
        ok: true,
        newKey,
        message: it.zone === 'inner' ? '已移入里书架 🌙（演示沙盒）' : '已移回表书架 ☀️（演示沙盒）',
      });
    }

    if (apiPath === '/api/item/create-text') {
      const cat = body.category || categories[0];
      const rawName = String(body.fileName || '新文稿.md').trim();
      const autoInner = rawName.startsWith('_');
      const cleanName = rawName.replace(/^_+/, '') || '新文稿.md';
      const finalName = /\.[a-z0-9]+$/i.test(cleanName) ? cleanName : cleanName + '.md';
      const z = autoInner ? 'inner' : body.zone === 'inner' ? 'inner' : 'outer';
      const fullPath = (z === 'inner' ? VAULT : ROOT) + '\\' + cat + '\\' + finalName;
      docBodies[fullPath] = String(body.content || '');
      items.unshift({
        id: 'demo-' + Date.now(),
        key: `${z}:${cat}\\${finalName}`,
        name: finalName,
        category: cat,
        subDir: '',
        zone: z,
        isExternal: false,
        isDirectory: false,
        existsOnDisk: true,
        fullPath,
        relPath: cat + '\\' + finalName,
        itemType: 'text',
        ext: '.md',
        size: String(body.content || '').length,
        updatedAt: '刚刚',
        note: String(body.note || ''),
        tags: String(body.tags || '')
          .split(/[,，\s]+/)
          .filter(Boolean),
        entry: '',
        detectedEntries: [],
        childCount: 0,
      });
      return jsonResp({
        ok: true,
        message: autoInner ? '检测到 _ 前缀，已自动剥离并收容入里书架 🌙' : '已存入书架（演示沙盒）',
      });
    }

    if (apiPath === '/api/item/meta') {
      const it = items.find((x) => x.key === body.key);
      if (it) {
        if (body.name) it.name = body.name;
        it.note = body.note || '';
        it.entry = body.entry || '';
        it.tags = String(body.tags || '')
          .split(/[,，\s]+/)
          .filter(Boolean);
      }
      return jsonResp({ ok: true, message: '备注与标签已更新' });
    }

    if (apiPath === '/api/item/move-category') {
      const it = items.find((x) => x.key === body.key);
      if (it && body.targetCategory) {
        it.category = body.targetCategory;
        it.subDir = body.targetSubDir || '';
      }
      return jsonResp({ ok: true, newKey: it?.key });
    }

    if (apiPath === '/api/category/create') {
      const clean = String(body.name || '').replace(/^_+/, '').trim();
      if (clean && !categories.includes(clean)) categories.push(clean);
      return jsonResp({ ok: true, message: `已创建分类「${clean}」` });
    }

    if (apiPath === '/api/category/rename') {
      const idx = categories.indexOf(body.oldName);
      const next = String(body.newName || '').replace(/^_+/, '').trim();
      if (idx >= 0 && next) {
        categories[idx] = next;
        items.forEach((x) => {
          if (x.category === body.oldName) x.category = next;
        });
      }
      return jsonResp({ ok: true, message: '分类已重命名' });
    }

    if (apiPath === '/api/category/delete') {
      const hasFiles = items.some((x) => x.category === body.name && !x.isExternal);
      if (hasFiles) return jsonResp({ ok: false, error: '该分类下仍有藏品，演示沙盒已拦截删除' });
      categories = categories.filter((c) => c !== body.name);
      return jsonResp({ ok: true, message: '已删除空分类' });
    }

    if (apiPath === '/api/action/open-explorer' || apiPath === '/api/action/run' || apiPath === '/api/dialog/pick') {
      return jsonResp({
        ok: false,
        error: '当前为浏览器在线体验版，下载 Windows 完整版（曜蚀阁.exe）即可调用本机目录与启动程序',
      });
    }

    return jsonResp({ ok: true, message: '演示操作已完成' });
  };
})();
