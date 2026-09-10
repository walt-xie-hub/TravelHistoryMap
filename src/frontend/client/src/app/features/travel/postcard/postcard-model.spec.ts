import type { TravelRecord } from '../models/travel-record.model';
import {
  POSTCARD_TRAIL_SNIPPET_MAX,
  buildPostcardTrail,
  changePostcardTemplate,
  createPostcardContent,
  pickMainRecord,
  postcardFileName,
  postcardImageBudget,
  postcardPlace,
  setMainRecord,
  sortNewestFirst,
  togglePostcardModule,
  toggleTrailRecord,
} from './postcard-model';
import { postcardTemplateById } from './postcard-template';

function record(partial: Partial<TravelRecord> & { id: number; arrivedAt: string }): TravelRecord {
  return {
    userId: 100008,
    locationName: `地点${partial.id}`,
    latitude: 23.1,
    longitude: 112.5,
    departedAt: null,
    description: null,
    ...partial,
  } as unknown as TravelRecord;
}

const older = record({
  id: 1,
  arrivedAt: '2026-09-05T08:00:00Z',
  locationName: '肇庆',
  city: '肇庆',
});
const newest = record({
  id: 2,
  arrivedAt: '2026-09-08T08:00:00Z',
  locationName: '中山市黄圃镇',
  city: '中山',
  description: '<p>和朋友吃了<b>脆肉鲩</b>，很鲜，值得再来一次。</p>',
  images: [
    {
      id: 21,
      originalFileName: 'a.jpg',
      contentType: 'image/jpeg',
      fileSize: 1,
      thumbnailUrl: '',
      originalUrl: '',
    },
    {
      id: 22,
      originalFileName: 'b.jpg',
      contentType: 'image/jpeg',
      fileSize: 1,
      thumbnailUrl: '',
      originalUrl: '',
    },
  ],
});
const middle = record({
  id: 3,
  arrivedAt: '2026-09-06T08:00:00Z',
  locationName: '珠海',
  city: '珠海',
});

const records = [older, newest, middle];

describe('postcardPlace（地点口径＝City 快照优先）', () => {
  it('有城市快照用城市，没有则用地点名快照', () => {
    expect(postcardPlace(newest)).toBe('中山');
    expect(postcardPlace(record({ id: 9, arrivedAt: '2026-01-01T00:00:00Z', city: '   ' }))).toBe(
      '地点9',
    );
  });
});

describe('pickMainRecord（主记录默认＝最新一条）', () => {
  it('默认取到达时间最新的一条', () => {
    expect(pickMainRecord(records)?.id).toBe(2);
  });

  it('用户指定的主记录优先；指定的不在集合里则回退最新', () => {
    expect(pickMainRecord(records, 1)?.id).toBe(1);
    expect(pickMainRecord(records, 999)?.id).toBe(2);
  });

  it('空集合返回 undefined（UI 里入口会被禁用）', () => {
    expect(pickMainRecord([])).toBeUndefined();
  });
});

describe('sortNewestFirst', () => {
  it('按到达时间倒序且不改动入参', () => {
    const input = [older, newest, middle];
    expect(sortNewestFirst(input).map((r) => r.id)).toEqual([2, 3, 1]);
    expect(input.map((r) => r.id)).toEqual([1, 2, 3]);
  });
});

describe('createPostcardContent（默认内容）', () => {
  it('主记录＝最新一条，地点/日期跟随主记录，清单默认勾选全部', () => {
    const content = createPostcardContent({ records });
    expect(content.templateId).toBe('classic-post');
    expect(content.mainRecordId).toBe(2);
    expect(content.fields.place).toBe('中山');
    expect(content.fields.date).toBe('2026.09.08');
    expect(content.fields.message).toBe('');
    expect(content.includedRecordIds).toEqual([2, 3, 1]);
  });

  it('票面年份＝主记录到达年份，生肖随之（2026 马）', () => {
    const content = createPostcardContent({ records });
    expect(content.stamp.year).toBe(2026);
    expect(content.stamp.zodiac).toBe('马');
  });

  it('图片默认取主记录的图片，并按模板张数截断', () => {
    // 默认模板（经典邮政）只用 1 张
    expect(createPostcardContent({ records }).imageIds).toEqual([21]);
    // 拼贴要 3 张，主记录只有 2 张 → 有几张用几张（缺的在 UI 提示）
    expect(createPostcardContent({ records, templateId: 'collage' }).imageIds).toEqual([21, 22]);
    // 纯文模板不用图
    expect(createPostcardContent({ records, templateId: 'back-plain' }).imageIds).toEqual([]);
  });

  it('模块开关＝模板默认值；模板不支持的一律关闭', () => {
    const polaroid = createPostcardContent({ records, templateId: 'polaroid' });
    // 邮票与邮戳默认开；清单看模板默认（拍立得默认不带）
    expect(polaroid.modules.postmark).toBe(true);
    expect(polaroid.modules.trail).toBe(false);
    expect(polaroid.modules.recipient).toBe(false);
    const backPlain = createPostcardContent({ records, templateId: 'back-plain' });
    expect(backPlain.modules.recipient).toBe(true);
    expect(backPlain.modules.coord).toBe(false);
  });
});

describe('changePostcardTemplate（换模板保留内容）', () => {
  it('字段、主记录、清单、票面都保留，只有模块与图片选择随模板走', () => {
    const content = {
      ...createPostcardContent({ records, templateId: 'classic-post' }),
      fields: { ...createPostcardContent({ records }).fields, message: '你好' },
    };
    const next = changePostcardTemplate(content, 'collage');
    expect(next.fields.message).toBe('你好');
    expect(next.mainRecordId).toBe(2);
    expect(next.includedRecordIds).toEqual([2, 3, 1]);
    expect(next.modules.trail).toBe(true); // collage 默认开清单
    expect(next.modules.coord).toBe(false); // collage 不支持坐标
  });

  it('用户改过的开关在新模板支持时保留、不支持时关闭', () => {
    const base = togglePostcardModule(
      createPostcardContent({ records, templateId: 'classic-post' }),
      'trail',
      true,
    );
    const next = changePostcardTemplate(base, 'back-plain');
    expect(next.modules.trail).toBe(true);
    const back = changePostcardTemplate(next, 'polaroid');
    expect(back.modules.recipient).toBe(false);
  });
});

describe('togglePostcardModule / setMainRecord / toggleTrailRecord', () => {
  it('模板不支持的模块开关是空操作', () => {
    const content = createPostcardContent({ records, templateId: 'polaroid' });
    expect(togglePostcardModule(content, 'recipient', true)).toBe(content);
  });

  it('换主记录会同步标题、地点、日期与票面年份（前提是这些字段还没被改过）', () => {
    const content = createPostcardContent({ records });
    const next = setMainRecord(content, records, 1);
    expect(next.mainRecordId).toBe(1);
    expect(next.fields.title).toBe('肇庆');
    expect(next.fields.place).toBe('肇庆');
    expect(next.fields.date).toBe('2026.09.05');
    expect(next.stamp.year).toBe(2026);
    expect(next.fields.message).toBe(content.fields.message);
  });

  it('用户改过的字段不因换主角被覆盖', () => {
    const base = createPostcardContent({ records });
    const edited = {
      ...base,
      fields: { ...base.fields, title: '我自己起的标题', place: '自定义地点', message: '你好' },
    };
    const next = setMainRecord(edited, records, 1);
    expect(next.fields.title).toBe('我自己起的标题');
    expect(next.fields.place).toBe('自定义地点');
    expect(next.fields.date).toBe('2026.09.05');
  });

  it('同一主记录再次传入是空操作（不重置已改的字段）', () => {
    const content = createPostcardContent({ records });
    expect(setMainRecord(content, records, content.mainRecordId)).toBe(content);
  });

  it('主记录不属于记录集时是空操作', () => {
    const content = createPostcardContent({ records });
    expect(setMainRecord(content, records, 999)).toBe(content);
  });

  it('勾选/取消勾选：不做重排（顺序仍由时间倒序决定）', () => {
    const content = createPostcardContent({ records });
    const withoutMiddle = toggleTrailRecord(content, 3);
    expect(withoutMiddle.includedRecordIds).toEqual([2, 1]);
    expect(toggleTrailRecord(withoutMiddle, 3).includedRecordIds).toEqual([2, 1, 3]);
  });
});

describe('buildPostcardTrail（清单按模板上限截断）', () => {
  it('时间倒序、序号从 1 起、正文摘录去标签并截断', () => {
    const trail = buildPostcardTrail(records, [1, 2, 3], 5);
    expect(trail.rows.map((row) => row.recordId)).toEqual([2, 3, 1]);
    expect(trail.rows[0]!.index).toBe(1);
    expect(trail.rows[0]!.place).toBe('中山市黄圃镇');
    // 正文里的标签按既有口径换成空格再折叠，摘录超 20 字截断加省略号
    expect(trail.rows[0]!.snippet).toBe(
      `${'和朋友吃了 脆肉鲩 ，很鲜，值得再来一次。'.slice(0, POSTCARD_TRAIL_SNIPPET_MAX)}…`,
    );
    expect(trail.hidden).toBe(0);
  });

  it('超出上限时截断并报出被隐藏的条数（版式显示「……还有 N 站旅程」）', () => {
    const trail = buildPostcardTrail(records, [1, 2, 3], 2);
    expect(trail.rows).toHaveLength(2);
    expect(trail.hidden).toBe(1);
  });

  it('上限为 0（异常配置）时不渲染任何行，全部计入 hidden', () => {
    const trail = buildPostcardTrail(records, [1, 2, 3], 0);
    expect(trail.rows).toEqual([]);
    expect(trail.hidden).toBe(3);
  });

  it('取消勾选的记录不出现；清单为空时 rows 为空', () => {
    expect(buildPostcardTrail(records, [2], 5).rows.map((row) => row.recordId)).toEqual([2]);
    expect(buildPostcardTrail(records, [], 5)).toEqual({ rows: [], hidden: 0 });
  });
});

describe('postcardImageBudget（图片不足只在 UI 提示，不阻止）', () => {
  it('算出还缺几张', () => {
    expect(postcardImageBudget(postcardTemplateById('collage'), 1)).toEqual({
      required: 3,
      available: 1,
      missing: 2,
    });
    expect(postcardImageBudget(postcardTemplateById('collage'), 5)).toEqual({
      required: 3,
      available: 5,
      missing: 0,
    });
    expect(postcardImageBudget(postcardTemplateById('back-plain'), 0)).toEqual({
      required: 0,
      available: 0,
      missing: 0,
    });
  });
});

describe('postcardFileName', () => {
  it('带地点与日期，非法字符替换为 -', () => {
    const content = createPostcardContent({ records });
    expect(postcardFileName(content)).toBe('travel-map-postcard-中山-2026.09.08.png');
    expect(
      postcardFileName(
        { ...content, fields: { ...content.fields, place: 'A/B C', date: '' } },
        'jpg',
      ),
    ).toBe('travel-map-postcard-A-B-C.jpg');
  });

  it('地点为空时回退 postcard', () => {
    const content = createPostcardContent({ records });
    expect(postcardFileName({ ...content, fields: { ...content.fields, place: '  ' } })).toBe(
      'travel-map-postcard-postcard-2026.09.08.png',
    );
  });
});
