/**
 * 地图标记的缩放分档标准（ADR-0004）。
 *
 * 地图缩放（AMap zoom 3–20）会让同一批标记在不同视野下需要不同的视觉权重：
 * 全览时点稠密，标记要小才不会互相遮挡；街区级视野点很少，标记要大才好点选。
 *
 * 三条设计约束（为什么不是别的做法，理由见 ADR-0004）：
 * - **按 zoom 固定分档**，不按“当前可见标记数量 / 重叠程度”自适应——固定分档可预测、不抖动；
 * - **徽标不与标记同比例缩放**——同比例在远档只有 12px、数字读不出来；
 * - **标记外框尺寸不随档位变化**，只有“画面层”缩放——点击热区在任何档位都不小于 42px。
 *
 * 档位边界与尺寸只在下方 `MARKER_TIERS` 定义一次（CSS 只消费它输出的变量）；
 * 面向人的同一张表记录在 ADR-0004，二者需同步。
 */
export type MarkerTierId = 'far' | 'mid' | 'near';

export interface MarkerTier {
  readonly id: MarkerTierId;
  /** 本档覆盖的最小 zoom（含）：也是 `markerTierFor` 查找档位时唯一使用的边界。 */
  readonly minZoom: number;
  /** 本档覆盖的最大 zoom（含）：用于描述区间，并保证三档连续覆盖 3–20。 */
  readonly maxZoom: number;
  /** 标记画面层边长（px）。 */
  readonly markerSize: number;
  /** 标记上计数 / ★ 徽标的最终尺寸（px）。 */
  readonly badgeSize: number;
}

/** 基准标记尺寸（px）：中档尺寸，改造前的固定值，也是 `--marker-scale` 的除数。 */
export const MARKER_BASE_SIZE = 42;

/** 基准徽标尺寸（px）：中档徽标尺寸，改造前的固定值，也是 `--badge-scale` 的分母。 */
export const BADGE_BASE_SIZE = 18;

const FAR_TIER: MarkerTier = { id: 'far', minZoom: 3, maxZoom: 7, markerSize: 28, badgeSize: 16 };
const MID_TIER: MarkerTier = { id: 'mid', minZoom: 8, maxZoom: 12, markerSize: 42, badgeSize: 18 };
const NEAR_TIER: MarkerTier = { id: 'near', minZoom: 13, maxZoom: 20, markerSize: 56, badgeSize: 22 };

/**
 * 唯一一份分档标准表（按 zoom 升序）：
 *
 * | 档位 | zoom  | 视野观感    | 标记 | 徽标 |
 * | ---- | ----- | ----------- | ---- | ---- |
 * | 远   | 3–7   | 全国 / 省域 | 28px | 16px |
 * | 中   | 8–12  | 城市 / 区县 | 42px | 18px |
 * | 近   | 13–20 | 街区        | 56px | 22px |
 *
 * 尺寸比固定 1 : 1.5 : 2；中档 42px 是改造前的固定尺寸，而应用自身的定位缩放
 * （`FOCUS_ZOOM = 10`、`TIMELINE_FOCUS_ZOOM = 12`）都落在中档，所以从侧栏或时光轴
 * 定位过去的观感与历史行为一致。
 */
export const MARKER_TIERS: readonly MarkerTier[] = [FAR_TIER, MID_TIER, NEAR_TIER];

/** 兜底档位：中档＝改造前的固定尺寸，也是异常值（`NaN` 等）时的安全选择。 */
export const DEFAULT_MARKER_TIER = MID_TIER;

/**
 * 由地图缩放值取标记档位：从近到远找**第一个 `minZoom <= zoom`** 的档。
 *
 * 用“起点”查找而不是区间包含，是为了处理 `setFitView` 给出的**小数 zoom**：
 * `7.5`、`12.4` 落在档位表格的空隙里（7 < zoom < 8、12 < zoom < 13），
 * 用区间包含会全部漏掉并错落到远档；按起点归属则分别得到远档（7.5 < 8）与中档。
 *
 * 越界值钳制到首 / 末档（zoom 低于 3 → 远档，高于 20 → 近档），
 * 异常值（`NaN` / `±Infinity`）回退到 `DEFAULT_MARKER_TIER`，任何输入都有确定结果。
 */
export function markerTierFor(zoom: number): MarkerTier {
  if (!Number.isFinite(zoom)) return DEFAULT_MARKER_TIER;
  return [...MARKER_TIERS].reverse().find((tier) => zoom >= tier.minZoom) ?? FAR_TIER;
}

/** `markerTierVars` 的输出：只暴露 `styles.scss` 真正消费的两个变量。 */
export type MarkerTierVars = Record<'--marker-scale' | '--badge-scale', string>;

/**
 * 把档位翻译成挂在 `.map-stage` 上的 CSS 自定义属性：
 * - `--marker-scale`：标记“画面层”（剪影、熊猫部件、附着其上的徽标）的整体缩放因子；
 * - `--badge-scale`：徽标**相对画面层**的额外缩放因子，把徽标拉回本档的标准尺寸。
 *
 * 合比因子在这里算好（`badgeSize / 18 / markerScale`），CSS 只需一次 `scale()`，
 * 样式里不做除法；因此“徽标最终尺寸 = `badgeSize`”这个不变量由单测守着。
 */
export function markerTierVars(tier: MarkerTier): MarkerTierVars {
  const markerScale = tier.markerSize / MARKER_BASE_SIZE;
  return {
    '--marker-scale': `${round3(markerScale)}`,
    '--badge-scale': `${round3(tier.badgeSize / BADGE_BASE_SIZE / markerScale)}`,
  };
}

function round3(value: number): number {
  return Math.round(value * 1000) / 1000;
}
