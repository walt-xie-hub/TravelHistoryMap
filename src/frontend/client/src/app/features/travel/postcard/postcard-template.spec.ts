import {
  POSTCARD_TEMPLATES,
  POSTCARD_MODULE_LABELS,
  resolveModules,
  postcardTemplateById,
  switchableModules,
  type PostcardModuleId,
} from './postcard-template';

describe('postcardTemplateById（ADR-0018：10 款模板）', () => {
  it('模板表有 10 款，id 唯一，名称不重复', () => {
    expect(POSTCARD_TEMPLATES).toHaveLength(10);
    expect(new Set(POSTCARD_TEMPLATES.map((t) => t.id)).size).toBe(10);
    expect(new Set(POSTCARD_TEMPLATES.map((t) => t.name)).size).toBe(10);
  });

  it('按 id 取模板；未知 id 回退到默认模板', () => {
    expect(postcardTemplateById('polaroid').name).toBe('拍立得');
    expect(postcardTemplateById('nope').id).toBe('classic-post');
  });

  it('版式族与风格覆盖到四类风格、两款背面式', () => {
    expect(POSTCARD_TEMPLATES.filter((t) => t.layout === 'back')).toHaveLength(2);
    expect(new Set(POSTCARD_TEMPLATES.map((t) => t.style))).toEqual(
      new Set(['vintage', 'journal', 'minimal', 'modern']),
    );
  });

  it('每个模板：默认模块是支持模块的子集；清单上限 1–5；图片数 0–3', () => {
    for (const template of POSTCARD_TEMPLATES) {
      for (const moduleId of template.defaults) {
        expect(template.modules).toContain(moduleId);
      }
      expect(template.trailLimit).toBeGreaterThanOrEqual(1);
      expect(template.trailLimit).toBeLessThanOrEqual(5);
      expect(template.imageCount).toBeGreaterThanOrEqual(0);
      expect(template.imageCount).toBeLessThanOrEqual(3);
    }
  });

  it('邮票与邮戳是所有模板的既定元素（默认开）', () => {
    for (const template of POSTCARD_TEMPLATES) {
      expect(template.modules).toContain('stamp');
      expect(template.modules).toContain('postmark');
      expect(template.defaults).toContain('stamp');
    }
  });

  it('收件人栏只在背面式出现（正面式声明了也会被 resolveModules 关掉，这里直接不允许）', () => {
    for (const template of POSTCARD_TEMPLATES) {
      expect(template.modules.includes('recipient')).toBe(template.layout === 'back');
    }
  });
});

describe('resolveModules（模块开关按模板能力收敛）', () => {
  const polaroid = postcardTemplateById('polaroid');
  const backLetter = postcardTemplateById('back-letter');

  it('没有用户意见时取模板默认值', () => {
    const resolved = resolveModules(polaroid);
    expect(resolved.caption).toBe(true);
    expect(resolved.stamp).toBe(true);
    expect(resolved.postmark).toBe(false);
    expect(resolved.trail).toBe(false);
  });

  it('模板不支持的模块一律关掉（换模板不会把版式撑破）', () => {
    const resolved = resolveModules(polaroid, { recipient: true, trail: true });
    expect(resolved.recipient).toBe(false);
    expect(resolved.trail).toBe(true);
  });

  it('保留用户在当前会话里改过的、且模板支持的开关', () => {
    const resolved = resolveModules(backLetter, { postmark: false, trail: false, coord: true });
    expect(resolved.postmark).toBe(false);
    expect(resolved.trail).toBe(false);
    expect(resolved.coord).toBe(false); // 背面式不支持坐标小字
    expect(resolved.recipient).toBe(true);
  });

  it('返回全部模块 id 的完整开关表（模板都要有确定值）', () => {
    const resolved = resolveModules(polaroid);
    expect(Object.keys(resolved).sort()).toEqual(Object.keys(POSTCARD_MODULE_LABELS).sort());
  });
});

describe('switchableModules（面板里能出现的开关）', () => {
  it('只列出模板支持的模块，且顺序与模块定义一致', () => {
    expect(switchableModules(postcardTemplateById('back-plain'))).toEqual([
      'trail',
      'stamp',
      'postmark',
      'recipient',
      'caption',
    ] satisfies PostcardModuleId[]);
  });

  it('声明即可用：有「旅行标识」开关的模板，版式里必须真的留了落点', () => {
    // 4 款留了落点的模板才声明 icon（空操作的开关会被这条守住）
    const withIcon = POSTCARD_TEMPLATES.filter((template) => template.modules.includes('icon')).map(
      (t) => t.id,
    );
    expect(withIcon).toEqual(['classic-post', 'air-mail', 'polaroid', 'collage']);
  });
});
