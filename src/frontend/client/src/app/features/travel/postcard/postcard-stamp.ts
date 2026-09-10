/**
 * 明信片面值 / 齿孔 / 生肖（ADR-0018）。
 *
 * 「当年的生效邮票」在本应用里的语义＝**该次旅程所在年份**的生肖纪念票：
 * 票面是中国邮政式排版（竖排「中国邮政 CHINA」、面值、年份、齿孔），
 * 但图案是**自绘的生肖剪影**，不是任何真实邮票的复刻——真实票面是中国邮政的版权作品，
 * 仓库也不持有其素材；想用真票的用户可以上传自己的图片覆盖（`customImageDataUrl`）。
 *
 * 纯函数，可被 vitest 直接覆盖。
 */

/** 十二生肖（1900 年为鼠年） */
export const ZODIAC_ANIMALS = [
  '鼠',
  '牛',
  '虎',
  '兔',
  '龙',
  '蛇',
  '马',
  '羊',
  '猴',
  '鸡',
  '狗',
  '猪',
] as const;

export type ZodiacAnimal = (typeof ZODIAC_ANIMALS)[number];

/** 生肖基准年：1900 = 庚子鼠年 */
const ZODIAC_EPOCH_YEAR = 1900;

/** 年份 → 生肖（对 1900 之前/之后的年份都成立） */
export function zodiacOf(year: number): ZodiacAnimal {
  const index = (((year - ZODIAC_EPOCH_YEAR) % 12) + 12) % 12;
  return ZODIAC_ANIMALS[index]!;
}

/** 可选面值（分位定价，像真票；默认 1.20 元） */
export const STAMP_DENOMINATIONS = ['0.80', '1.20', '1.50', '2.00', '3.00'] as const;

export const DEFAULT_STAMP_DENOMINATION = '1.20';

/** 上传邮票图片的规格（与 staged-images 的图片口径一致，单张更小） */
export const STAMP_IMAGE_MAX_BYTES = 2 * 1024 * 1024;
export const STAMP_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const;

/** 票面（明信片上那枚邮票） */
export interface PostcardStampFace {
  /** 发行年份 —— 默认取主记录到达年份（跨年旅程取起点） */
  year: number;
  readonly zodiac: ZodiacAnimal;
  /** 面值（元），文本形式便于直接渲染 */
  denomination: string;
  /** 用户上传的自备票图（dataURL）；为空时用自绘生肖票面 */
  customImageDataUrl: string | null;
  /** 上传文件的原名（仅用于展示"已换用自己的图片"，不参与渲染） */
  customImageName: string | null;
}

export function defaultStampFace(
  year: number,
  denomination: string = DEFAULT_STAMP_DENOMINATION,
): PostcardStampFace {
  return {
    year,
    zodiac: zodiacOf(year),
    denomination,
    customImageDataUrl: null,
    customImageName: null,
  };
}

/** 改年份时生肖必须跟着走（票面自洽），面值与上传图保持不变 */
export function withStampYear(face: PostcardStampFace, year: number): PostcardStampFace {
  return { ...face, year, zodiac: zodiacOf(year) };
}

export function withStampDenomination(
  face: PostcardStampFace,
  denomination: string,
): PostcardStampFace {
  return { ...face, denomination };
}

export interface StampImageValidation {
  accepted: boolean;
  error?: string;
}

/** 上传票图规格校验（格式 + 体积），与 staged-images.util 同样的"先校验再使用"口径 */
export function validateStampImage(field: { type: string; size: number }): StampImageValidation {
  if (!(STAMP_IMAGE_TYPES as readonly string[]).includes(field.type)) {
    return { accepted: false, error: '邮票图片仅支持 JPEG / PNG / WebP。' };
  }
  if (field.size > STAMP_IMAGE_MAX_BYTES) {
    return { accepted: false, error: '邮票图片不能超过 2MB。' };
  }
  return { accepted: true };
}

/** 票面文字：年份 + 生肖 + 面值（无障碍文案与图例共用，保证口径一致） */
export function stampCaption(face: PostcardStampFace): string {
  return face.customImageDataUrl
    ? `自备邮票图（${face.year} 年）`
    : `${face.year} 年 ${face.zodiac} 年纪念邮票，面值 ${face.denomination} 元`;
}

/**
 * 生肖 → 图标库里**已有**的剪影（ADR-0016 的 Icon library）。
 * 目前只有虎与猴能对上；其余生肖票面用生肖汉字 + 邮政式排版，
 * 不为邮票新造 12 个形状（避免为装饰再维护一套图形资产）。
 */
const ZODIAC_ICON_KEYS: Readonly<Partial<Record<ZodiacAnimal, string>>> = {
  虎: 'animal-tiger',
  猴: 'animal-golden-monkey',
};

export function zodiacIconKey(zodiac: ZodiacAnimal): string | null {
  return ZODIAC_ICON_KEYS[zodiac] ?? null;
}
