/**
 * 明信片内容模型（ADR-0018）。
 *
 * 明信片 = 模板（版式与能力） + 内容（记录、图片、字段、模块开关、票面）。
 * 这里负责：默认值怎么来（主记录＝最新一条）、换模板时什么该保留、
 * 足迹清单怎么按模板上限截断、导出文件名。
 *
 * 纯函数，可被 vitest 直接覆盖。
 */
import type { TravelRecord } from '../models/travel-record.model';
import { plainTextSnippet } from '../utils/rich-text.util';
import { fmtDate, fmtRange } from '../utils/travel-display';
import { postmarkDateText } from './postcard-postmark';
import {
  DEFAULT_POSTCARD_TEMPLATE_ID,
  postcardTemplateById,
  resolveModules,
  templateSupports,
  userModuleOverrides,
  type PostcardModuleId,
  type PostcardTemplate,
} from './postcard-template';
import { defaultStampFace, withStampYear, type PostcardStampFace } from './postcard-stamp';

/** 寄语上限：明信片手写的合理容量，超出截断而不是让版式溢出 */
export const POSTCARD_MESSAGE_MAX = 120;
export const POSTCARD_TITLE_MAX = 30;
export const POSTCARD_SIGNATURE_MAX = 20;
export const POSTCARD_RECIPIENT_MAX = 20;
export const POSTCARD_ADDRESS_MAX = 40;
export const POSTCARD_POSTAL_CODE_MAX = 6;
/** 地点写进邮戳上弧、日期写进下弧，都受弧线长度限制，因此单独给上限 */
export const POSTCARD_PLACE_MAX = 24;
export const POSTCARD_DATE_MAX = 16;
/** 清单行里的正文摘录字数 */
export const POSTCARD_TRAIL_SNIPPET_MAX = 20;

/** 明信片上的文字字段（结构化，不做富文本：版式固定，字段才放得稳） */
export interface PostcardFields {
  readonly title: string;
  readonly message: string;
  readonly signature: string;
  readonly recipient: string;
  readonly address: string;
  readonly postalCode: string;
  /** 日期文本（可改）：默认主记录到达日期 */
  readonly date: string;
  /** 地点文本（可改）：默认主记录 City 快照 / 地点名 */
  readonly place: string;
}

export interface PostcardTrailRow {
  /** 1 起的序号（版式直接渲染） */
  readonly index: number;
  readonly recordId: number;
  readonly place: string;
  readonly range: string;
  /** 正文摘录（无正文时为空串） */
  readonly snippet: string;
}

export interface PostcardTrail {
  readonly rows: readonly PostcardTrailRow[];
  /** 因模板行数上限被截掉的条数（>0 时版式显示「……还有 N 站旅程」） */
  readonly hidden: number;
}

export interface PostcardContent {
  readonly templateId: string;
  /** 主记录：决定主图、邮戳地名与日期、"寄出地"的语义 */
  readonly mainRecordId: number;
  /** 勾选进足迹清单的记录（时间倒序由 `buildPostcardTrail` 保证，UI 不允许重排） */
  readonly includedRecordIds: readonly number[];
  readonly fields: PostcardFields;
  readonly modules: Readonly<Record<PostcardModuleId, boolean>>;
  /** 选中的图片（按模板 imageCount 取前 N 张） */
  readonly imageIds: readonly number[];
  readonly stamp: PostcardStampFace;
}

/** 地点显示：City 快照优先，无快照用地点名快照（邮戳与标题同口径） */
export function postcardPlace(record: TravelRecord): string {
  return record.city?.trim() || record.locationName;
}

/** 时间倒序（最新的在最前）——清单与主记录默认值都基于它 */
export function sortNewestFirst(records: readonly TravelRecord[]): TravelRecord[] {
  return [...records].sort((a, b) => b.arrivedAt.localeCompare(a.arrivedAt));
}

/** 主记录：优先用户指定的那条，否则取最新一条（与时光轴 newestOf 同口径） */
export function pickMainRecord(
  records: readonly TravelRecord[],
  preferredId?: number | null,
): TravelRecord | undefined {
  const sorted = sortNewestFirst(records);
  if (preferredId != null) {
    const preferred = sorted.find((record) => record.id === preferredId);
    if (preferred) return preferred;
  }
  return sorted[0];
}

/** 默认字段：全部来自主记录，用户可在编辑器里改 */
export function defaultPostcardFields(main: TravelRecord | undefined): PostcardFields {
  return {
    title: main ? main.locationName : '',
    message: '',
    signature: '',
    recipient: '',
    address: '',
    postalCode: '',
    date: main ? postmarkDateText(fmtDate(main.arrivedAt)) : '',
    place: main ? postcardPlace(main) : '',
  };
}

export interface CreatePostcardOptions {
  readonly records: readonly TravelRecord[];
  readonly templateId?: string;
  /** 用户已改过的模块开关（换模板时保留） */
  readonly modules?: Partial<Record<PostcardModuleId, boolean>>;
  readonly mainRecordId?: number | null;
  readonly fields?: PostcardFields;
  readonly imageIds?: readonly number[];
}

/**
 * 构造明信片内容：主记录＝最新一条（或指定），清单默认勾选全部记录，
 * 模块开关按模板能力收敛（不支持的一律关掉）。
 */
export function createPostcardContent(options: CreatePostcardOptions): PostcardContent {
  const template = postcardTemplateById(options.templateId ?? DEFAULT_POSTCARD_TEMPLATE_ID);
  const records = sortNewestFirst(options.records);
  const main = pickMainRecord(records, options.mainRecordId);
  const imageIds = options.imageIds ?? (main?.images ?? []).map((image) => image.id);
  return {
    templateId: template.id,
    mainRecordId: main?.id ?? 0,
    includedRecordIds: records.map((record) => record.id),
    fields: options.fields ?? defaultPostcardFields(main),
    modules: resolveModules(template, options.modules),
    imageIds: fitImageIds(imageIds, template),
    stamp: defaultStampFace(
      main ? new Date(main.arrivedAt).getFullYear() : new Date().getFullYear(),
    ),
  };
}

/**
 * 换模板：内容全部保留（字段、清单勾选、主记录、票面），只有两件事随模板走——
 * ① 模块开关按新模板的能力重新收敛（用户已改过且新模板支持的保留）；
 * ② 图片选择按新模板的张数截断/保留（不足由 UI 提示，不阻止）。
 */
export function changePostcardTemplate(
  content: PostcardContent,
  templateId: string,
): PostcardContent {
  const previous = postcardTemplateById(content.templateId);
  const template = postcardTemplateById(templateId);
  return {
    ...content,
    templateId: template.id,
    modules: resolveModules(template, userModuleOverrides(previous, content.modules)),
    imageIds: fitImageIds(content.imageIds, template),
  };
}

/** 切换模块开关：模板不支持时忽略（面板也不会显示该开关） */
export function togglePostcardModule(
  content: PostcardContent,
  moduleId: PostcardModuleId,
  enabled: boolean,
): PostcardContent {
  const template = postcardTemplateById(content.templateId);
  if (!templateSupports(template, moduleId)) return content;
  return { ...content, modules: { ...content.modules, [moduleId]: enabled } };
}

/** 勾选/取消勾选清单里的一条记录（不允许重排；主记录也可取消，清单为空即不渲染） */
export function toggleTrailRecord(content: PostcardContent, recordId: number): PostcardContent {
  const included = content.includedRecordIds.includes(recordId)
    ? content.includedRecordIds.filter((id) => id !== recordId)
    : [...content.includedRecordIds, recordId];
  return { ...content, includedRecordIds: included };
}

/**
 * 换主记录：主记录必须来自记录集（调用方传的就是记录集），票面年份随之更新。
 *
 * 标题 / 地点 / 日期三项在换主角时跟随新记录，但**只在用户没改过时**跟随：
 * 与旧主角派生的默认值相同＝没改过（与模块开关同一套"默认值不算用户意见"的思路）。
 */
export function setMainRecord(
  content: PostcardContent,
  records: readonly TravelRecord[],
  recordId: number,
): PostcardContent {
  const main = records.find((record) => record.id === recordId);
  if (!main) return content;
  if (content.mainRecordId === recordId) return content;

  const previous = records.find((record) => record.id === content.mainRecordId);
  const defaults = defaultPostcardFields(previous);
  const fields: PostcardFields = {
    ...content.fields,
    title: content.fields.title === defaults.title ? main.locationName : content.fields.title,
    place: content.fields.place === defaults.place ? postcardPlace(main) : content.fields.place,
    date:
      content.fields.date === defaults.date
        ? postmarkDateText(fmtDate(main.arrivedAt))
        : content.fields.date,
  };
  return {
    ...content,
    mainRecordId: recordId,
    fields,
    stamp: withStampYear(content.stamp, new Date(main.arrivedAt).getFullYear()),
  };
}

/** 按模板行数上限截断清单：行数按时间倒序，超出部分折算成「……还有 N 站旅程」 */
export function buildPostcardTrail(
  records: readonly TravelRecord[],
  includedRecordIds: readonly number[],
  limit: number,
): PostcardTrail {
  const included = new Set(includedRecordIds);
  const picked = sortNewestFirst(records).filter((record) => included.has(record.id));
  const rows = picked.slice(0, Math.max(0, limit)).map((record, position) => ({
    index: position + 1,
    recordId: record.id,
    place: record.locationName,
    range: fmtRange(record),
    snippet: plainTextSnippet(record.description, POSTCARD_TRAIL_SNIPPET_MAX),
  }));
  return { rows, hidden: picked.length - rows.length };
}

/** 图片预算：模板要几张、现在有几张、还缺几张（不足不阻止导出，只在 UI 提示） */
export interface PostcardImageBudget {
  readonly required: number;
  readonly available: number;
  readonly missing: number;
}

export function postcardImageBudget(
  template: PostcardTemplate,
  available: number,
): PostcardImageBudget {
  const missing = Math.max(0, template.imageCount - available);
  return { required: template.imageCount, available, missing };
}

/** 导出文件名：travel-map-postcard-{地点}-{日期}.png（非法字符换成 -） */
export function postcardFileName(content: PostcardContent, extension = 'png'): string {
  const place = sanitizeFilePart(content.fields.place) || 'postcard';
  const date = sanitizeFilePart(content.fields.date);
  return `travel-map-postcard-${place}${date ? `-${date}` : ''}.${extension}`;
}

function sanitizeFilePart(value: string): string {
  return value
    .trim()
    .replace(/[\s/\\:*?"<>|]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

/** 图片选择按模板张数收敛：多了截断，少了保留（UI 提示缺几张） */
function fitImageIds(imageIds: readonly number[], template: PostcardTemplate): readonly number[] {
  return template.imageCount === 0 ? [] : imageIds.slice(0, template.imageCount);
}
