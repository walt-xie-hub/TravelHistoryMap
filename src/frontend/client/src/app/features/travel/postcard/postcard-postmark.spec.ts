import {
  POSTMARK_NOTE,
  POSTMARK_SIZE,
  arcPath,
  postmarkDateText,
  postmarkGeometry,
  postmarkText,
  stampCancelAngle,
} from './postcard-postmark';

describe('arcPath（弧线文字路径）', () => {
  it('从起点画到终点，半径写进 A 指令；上弧顺时针、下弧反向扫掠', () => {
    expect(arcPath(100, 202, 338, true)).toBe('M -92.72 -37.46 A 100 100 0 0 1 92.72 -37.46');
    expect(arcPath(100, 158, 22, false)).toBe('M -92.72 37.46 A 100 100 0 0 0 92.72 37.46');
  });

  it('终点在起点的左侧时（下弧读向仍从左到右）不会退化成大弧', () => {
    expect(arcPath(50, 160, 20, false)).not.toContain(' 1 0 ');
  });

  it('跨度超过 180° 时用大弧标志，避免 SVG 走错一侧', () => {
    expect(arcPath(10, 0, 200, true)).toContain('A 10 10 0 1 1');
  });
});

describe('postmarkGeometry（日戳几何）', () => {
  it('默认直径 180px，弧线半径在外圈之内、内圈之外', () => {
    const geometry = postmarkGeometry();
    expect(geometry.size).toBe(POSTMARK_SIZE);
    expect(geometry.arcRadius).toBeLessThan(POSTMARK_SIZE / 2);
    expect(geometry.innerRadius).toBeLessThan(geometry.arcRadius);
    expect(geometry.noteWidth).toBeLessThan(POSTMARK_SIZE);
  });

  it('尺寸可缩放：直径翻倍时半径与横条同步翻倍', () => {
    const small = postmarkGeometry(120);
    const large = postmarkGeometry(240);
    expect(large.arcRadius).toBeCloseTo((small.arcRadius + 11) * 2 - 11, 5);
    expect(large.noteWidth).toBeCloseTo(small.noteWidth * 2, 5);
  });
});

describe('stampCancelAngle（销票角度稳定但看起来随手盖的）', () => {
  it('同一条记录稳定，取值范围落在 -14° ~ -6°', () => {
    expect(stampCancelAngle(42)).toBe(stampCancelAngle(42));
    for (const seed of [0, 1, 3, 7, 123456]) {
      const angle = stampCancelAngle(seed);
      expect(angle).toBeLessThanOrEqual(-6);
      expect(angle).toBeGreaterThanOrEqual(-14);
    }
  });

  it('不同记录会得到不同角度（错开铺满 -6°~-14° 的档位）', () => {
    const angles = new Set(Array.from({ length: 1000 }, (_, i) => stampCancelAngle(i)));
    expect(angles.size).toBeGreaterThanOrEqual(6);
    for (const angle of angles) {
      expect(angle).toBeLessThanOrEqual(-6);
      expect(angle).toBeGreaterThanOrEqual(-14);
    }
  });

  it('负数 id（异常输入）不产生 NaN', () => {
    expect(Number.isFinite(stampCancelAngle(-7))).toBe(true);
  });
});

describe('postmarkText / postmarkDateText（邮戳口径）', () => {
  it('地名 + 日期 + 纪念字样', () => {
    expect(postmarkText('中山', postmarkDateText('2026-09-05'))).toBe(
      `中山 2026.09.05 ${POSTMARK_NOTE}`,
    );
  });

  it('日期按日戳习惯写成点分', () => {
    expect(postmarkDateText('2026-09-05')).toBe('2026.09.05');
  });
});
