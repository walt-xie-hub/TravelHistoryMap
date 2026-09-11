import {
  DEFAULT_STAMP_DENOMINATION,
  STAMP_IMAGE_MAX_BYTES,
  defaultStampFace,
  stampCaption,
  stampGeometry,
  stampViewBox,
  validateStampImage,
  withStampDenomination,
  withStampYear,
  zodiacIconKey,
  zodiacOf,
} from './postcard-stamp';

describe('zodiacOf（生肖＝"当年生效"的语义）', () => {
  it('基准年与已知年份', () => {
    expect(zodiacOf(1900)).toBe('鼠');
    expect(zodiacOf(2024)).toBe('龙');
    expect(zodiacOf(2025)).toBe('蛇');
    expect(zodiacOf(2026)).toBe('马');
    expect(zodiacOf(2035)).toBe('兔');
  });

  it('12 年一循环，且 1900 年之前也成立', () => {
    expect(zodiacOf(2026)).toBe(zodiacOf(2014));
    expect(zodiacOf(1900 - 12)).toBe('鼠');
    expect(zodiacOf(1899)).toBe('猪');
  });
});

describe('defaultStampFace / withStampYear（票面自洽）', () => {
  it('默认面值 1.20 元，生肖随年份', () => {
    const face = defaultStampFace(2026);
    expect(face).toMatchObject({
      year: 2026,
      zodiac: '马',
      denomination: DEFAULT_STAMP_DENOMINATION,
      customImageDataUrl: null,
    });
  });

  it('改年份必须同时改生肖（否则票面自相矛盾）', () => {
    const face = withStampYear(defaultStampFace(2026), 2024);
    expect(face.year).toBe(2024);
    expect(face.zodiac).toBe('龙');
  });

  it('改年份/面值不影响用户上传的自备票图', () => {
    const custom = {
      ...defaultStampFace(2026),
      customImageDataUrl: 'data:image/png;base64,AAA',
      customImageName: 'mine.png',
    };
    const next = withStampDenomination(withStampYear(custom, 2025), '2.00');
    expect(next.customImageName).toBe('mine.png');
    expect(next.denomination).toBe('2.00');
    expect(next.zodiac).toBe('蛇');
  });
});

describe('validateStampImage（上传票图规格）', () => {
  it('接受 jpeg / png / webp 且不超过 2MB', () => {
    expect(validateStampImage({ type: 'image/png', size: 1024 }).accepted).toBe(true);
    expect(validateStampImage({ type: 'image/webp', size: STAMP_IMAGE_MAX_BYTES }).accepted).toBe(
      true,
    );
  });

  it('拒绝其它格式与超大文件，并给出可读原因', () => {
    expect(validateStampImage({ type: 'image/gif', size: 10 })).toEqual({
      accepted: false,
      error: '邮票图片仅支持 JPEG / PNG / WebP。',
    });
    expect(validateStampImage({ type: 'image/jpeg', size: STAMP_IMAGE_MAX_BYTES + 1 })).toEqual({
      accepted: false,
      error: '邮票图片不能超过 2MB。',
    });
  });
});

describe('stampCaption（无障碍与图例同一口径）', () => {
  it('自绘票说明年份/生肖/面值；自备票说明是自备', () => {
    expect(stampCaption(defaultStampFace(2026))).toBe('2026 年 马 年纪念邮票，面值 1.20 元');
    expect(
      stampCaption({ ...defaultStampFace(2026), customImageDataUrl: 'data:image/png;base64,AAA' }),
    ).toBe('自备邮票图（2026 年）');
  });
});

describe('stampGeometry（邮票齿孔：沿四条边等距的缺口）', () => {
  it('四角各有一个齿（间距按边长均分）', () => {
    const geometry = stampGeometry(100, 60, 5, 20);
    for (const corner of [
      { cx: 0, cy: 0 },
      { cx: 100, cy: 0 },
      { cx: 0, cy: 60 },
      { cx: 100, cy: 60 },
    ]) {
      expect(geometry.teeth).toContainEqual(corner);
    }
  });

  it('缺口圆心全部落在边线上（不会咬到票面内部）', () => {
    const geometry = stampGeometry(120, 80, 6, 30);
    for (const tooth of geometry.teeth) {
      const onEdge = tooth.cx === 0 || tooth.cx === 120 || tooth.cy === 0 || tooth.cy === 80;
      expect(onEdge).toBe(true);
      expect(tooth.cx).toBeGreaterThanOrEqual(0);
      expect(tooth.cx).toBeLessThanOrEqual(120);
      expect(tooth.cy).toBeGreaterThanOrEqual(0);
      expect(tooth.cy).toBeLessThanOrEqual(80);
    }
  });

  it('上下边齿数相同、左右边内部齿数相同（几何对称）', () => {
    const geometry = stampGeometry(120, 80, 6, 30);
    const atTop = geometry.teeth.filter((tooth) => tooth.cy === 0).length;
    const atBottom = geometry.teeth.filter((tooth) => tooth.cy === 80).length;
    const leftInner = geometry.teeth.filter((tooth) => tooth.cx === 0 && tooth.cy !== 0 && tooth.cy !== 80).length;
    const rightInner = geometry.teeth.filter((tooth) => tooth.cx === 120 && tooth.cy !== 0 && tooth.cy !== 80).length;
    expect(atTop).toBe(atBottom);
    expect(leftInner).toBe(rightInner);
    // 四角只在上下边出现一次，左右边不再重复放角上的齿
    expect(leftInner).toBeGreaterThan(0);
    expect(geometry.teeth.filter((tooth) => tooth.cx === 0 && tooth.cy === 0)).toHaveLength(1);
  });

  it('默认尺寸下齿数合理（约每 30px 一个，含四角）', () => {
    const geometry = stampGeometry();
    expect(geometry.width).toBe(236);
    expect(geometry.height).toBe(288);
    expect(geometry.teeth.length).toBeGreaterThan(20);
    expect(geometry.teeth.length).toBeLessThan(50);
  });

  it('viewBox 从原点起（与邮戳同样的绝对坐标策略，导出才不走样）', () => {
    expect(stampViewBox(stampGeometry(236, 288))).toBe('0 0 236 288');
  });
});

describe('zodiacIconKey（能对上图标库的生肖才用剪影）', () => {
  it('虎、猴复用图标库已有剪影', () => {
    expect(zodiacIconKey('虎')).toBe('animal-tiger');
    expect(zodiacIconKey('猴')).toBe('animal-golden-monkey');
  });

  it('其余生肖没有剪影（票面走生肖汉字），不为装饰新造形状', () => {
    for (const zodiac of ['鼠', '牛', '兔', '龙', '蛇', '马', '羊', '鸡', '狗', '猪'] as const) {
      expect(zodiacIconKey(zodiac)).toBeNull();
    }
  });
});
