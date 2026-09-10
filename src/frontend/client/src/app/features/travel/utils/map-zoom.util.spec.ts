import {
  BADGE_BASE_SIZE,
  MARKER_BASE_SIZE,
  MARKER_TIERS,
  markerTierFor,
  markerTierVars,
} from './map-zoom.util';

describe('markerTierFor（ADR-0004：标记随缩放分三级）', () => {
  it('远档：3–7（全国/省域）', () => {
    expect(markerTierFor(3).id).toBe('far');
    expect(markerTierFor(5).id).toBe('far');
    expect(markerTierFor(7).id).toBe('far');
  });

  it('中档：8–12（城市/区县）——现有定位 zoom 10 / 12 都落在这里', () => {
    expect(markerTierFor(8).id).toBe('mid');
    expect(markerTierFor(10).id).toBe('mid');
    expect(markerTierFor(12).id).toBe('mid');
  });

  it('近档：13 及以上（街区）', () => {
    expect(markerTierFor(13).id).toBe('near');
    expect(markerTierFor(20).id).toBe('near');
  });

  it('小数 zoom（setFitView 会给出小数）落在档位空隙时按起点归属，不会错落到远档', () => {
    expect(markerTierFor(7.5).id).toBe('far');
    expect(markerTierFor(8.2).id).toBe('mid');
    expect(markerTierFor(10.4).id).toBe('mid');
    expect(markerTierFor(12.4).id).toBe('mid');
    expect(markerTierFor(13.1).id).toBe('near');
  });

  it('越界钳制到首/末档（AMap 实际范围之外也不会有未定义档位）', () => {
    expect(markerTierFor(0).id).toBe('far');
    expect(markerTierFor(-3).id).toBe('far');
    expect(markerTierFor(21).id).toBe('near');
    expect(markerTierFor(99).id).toBe('near');
  });

  it('异常缩放值回退到中档（等于改造前的固定尺寸，安全选择）', () => {
    expect(markerTierFor(Number.NaN).id).toBe('mid');
    expect(markerTierFor(Number.POSITIVE_INFINITY).id).toBe('mid');
    expect(markerTierFor(Number.NEGATIVE_INFINITY).id).toBe('mid');
  });

  it('三档尺寸标准：28 / 42 / 56（比 1 : 1.5 : 2），徽标 16 / 18 / 22', () => {
    expect(MARKER_TIERS.map((tier) => tier.markerSize)).toEqual([28, 42, 56]);
    expect(MARKER_TIERS.map((tier) => tier.badgeSize)).toEqual([16, 18, 22]);
  });

  it('三档区间连续覆盖 3–20，不留空隙也不重叠', () => {
    expect(MARKER_TIERS[0]!.minZoom).toBe(3);
    expect(MARKER_TIERS.at(-1)!.maxZoom).toBe(20);
    for (let i = 1; i < MARKER_TIERS.length; i++) {
      expect(MARKER_TIERS[i]!.minZoom).toBe(MARKER_TIERS[i - 1]!.maxZoom + 1);
    }
  });
});

describe('markerTierVars（把档位变成 CSS 变量）', () => {
  it('输出画面层缩放因子与徽标的额外缩放因子（徽标最终尺寸 = 18 × 两个因子）', () => {
    expect(markerTierVars(markerTierFor(5))).toEqual({
      '--marker-scale': '0.667',
      '--badge-scale': '1.333',
    });
    expect(markerTierVars(markerTierFor(10))).toEqual({
      '--marker-scale': '1',
      '--badge-scale': '1',
    });
    expect(markerTierVars(markerTierFor(18))).toEqual({
      '--marker-scale': '1.333',
      '--badge-scale': '0.917',
    });
  });

  it('与标准表自洽：42 × 画面层缩放 = 标记尺寸，18 × 两个缩放 = 徽标尺寸', () => {
    for (const tier of MARKER_TIERS) {
      const vars = markerTierVars(tier);
      const markerScale = Number(vars['--marker-scale']);
      const badgeScale = Number(vars['--badge-scale']);
      // 变量按 3 位小数输出，因此容差取到 0.05（3 位小数 × 42 ≈ 0.021）
      expect(MARKER_BASE_SIZE * markerScale).toBeCloseTo(tier.markerSize, 1);
      expect(BADGE_BASE_SIZE * markerScale * badgeScale).toBeCloseTo(tier.badgeSize, 1);
    }
  });
});
