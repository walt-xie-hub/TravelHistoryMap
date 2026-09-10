import {
  DEFAULT_STAMP_DENOMINATION,
  STAMP_IMAGE_MAX_BYTES,
  defaultStampFace,
  stampCaption,
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
