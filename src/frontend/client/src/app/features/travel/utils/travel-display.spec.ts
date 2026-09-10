import type { TravelRecord } from '../models/travel-record.model';
import { effectiveCity, groupMapMarkers, iconSourceOf } from './travel-display';

function record(id: number, city: string | null, lng: number, lat: number, arrivedAt: string): TravelRecord {
  return {
    id,
    city,
    longitude: lng,
    latitude: lat,
    arrivedAt,
    departedAt: '2026-01-02T00:00:00Z',
    locationName: `p${id}`,
    isFavorite: false,
    tags: [],
  } as unknown as TravelRecord;
}

describe('groupMapMarkers（ADR-0017）', () => {
  it('有 City 快照的记录按城市合成一个城市标记，锚点为质心', () => {
    const groups = groupMapMarkers([
      record(1, '上海', 121.4737, 31.2304, '2026-03-03T00:00:00Z'),
      record(2, '上海', 121.5, 31.2, '2026-03-02T00:00:00Z'),
      record(3, '上海', 121.44, 31.26, '2026-03-01T00:00:00Z'),
    ]);

    expect(groups).toHaveLength(1);
    const [city] = groups;
    expect(city?.kind).toBe('city');
    expect(city?.city).toBe('上海');
    expect(city?.records.map((r) => r.id)).toEqual([1, 2, 3]);
    expect(city?.lng).toBeCloseTo(121.4712, 4);
    expect(city?.lat).toBeCloseTo(31.2301, 4);
  });

  it('无 City 的记录用派生城市并入城市标记（黄圃镇→中山）', () => {
    const groups = groupMapMarkers(
      [
        record(10, '中山', 113.39, 22.52, '2026-03-03T00:00:00Z'),
        record(11, null, 113.339146, 22.71151, '2026-03-01T00:00:00Z'),
      ],
      new Map([[11, '中山']]),
    );

    expect(groups).toHaveLength(1);
    expect(groups[0]?.key).toBe('city:中山');
    expect(groups[0]?.city).toBe('中山');
    expect(groups[0]?.records.map((r) => r.id)).toEqual([10, 11]);
  });

  it('快照优先于派生城市（快照=中山、派生=江门时仍归中山）', () => {
    const groups = groupMapMarkers(
      [
        record(20, '中山', 113.4, 22.5, '2026-03-02T00:00:00Z'),
        record(21, null, 113.1, 22.6, '2026-03-01T00:00:00Z'),
      ],
      new Map([[21, '江门']]),
    );

    expect(groups.map((g) => g.key).sort()).toEqual(['city:中山', 'city:江门']);
    expect(groups.find((g) => g.key === 'city:中山')?.records.map((r) => r.id)).toEqual([20]);
    expect(groups.find((g) => g.key === 'city:江门')?.records.map((r) => r.id)).toEqual([21]);
  });

  it('派生结果缺失或无效（undefined/空串）时退回按坐标合并的地点标记', () => {
    const groups = groupMapMarkers(
      [
        record(30, null, 100.00001, 20.00001, '2026-03-02T00:00:00Z'),
        record(31, null, 100.00002, 20.00002, '2026-03-01T00:00:00Z'),
        record(32, null, 101, 21, '2026-02-01T00:00:00Z'),
      ],
      new Map([
        [30, ''],
        [31, undefined as unknown as string],
      ]),
    );

    expect(groups).toHaveLength(2);
    const merged = groups.find((g) => g.kind === 'place' && g.lng === 100);
    expect(merged?.records.map((r) => r.id)).toEqual([30, 31]);
    expect(merged?.city).toBeUndefined();
  });

  it('组内与组间都按到达时间倒序（不依赖入参顺序）', () => {
    const input = [
      record(41, '上海', 121.4, 31.2, '2026-03-01T00:00:00Z'),
      record(42, null, 101, 21, '2026-02-01T00:00:00Z'),
      record(43, '上海', 121.5, 31.3, '2026-03-05T00:00:00Z'),
      record(44, null, 101, 21, '2026-02-10T00:00:00Z'),
    ];
    const groups = groupMapMarkers(input);

    expect(groups.map((g) => g.records[0]?.id)).toEqual([43, 44]);
    expect(groups[0]?.records.map((r) => r.id)).toEqual([43, 41]);
    expect(groups[1]?.records.map((r) => r.id)).toEqual([44, 42]);
  });
});

describe('iconSourceOf（ADR-0017：城市标记取首条记录的图标）', () => {
  const group = groupMapMarkers([
    record(1, '北京', 116.4, 39.9, '2026-03-03T00:00:00Z'),
    record(2, '北京', 116.39, 39.91, '2026-03-01T00:00:00Z'),
  ])[0]!;

  it('取该城最早（首次到访）的一条记录', () => {
    expect(group.kind).toBe('city');
    expect(iconSourceOf(group).id).toBe(2);
  });
});

describe('effectiveCity（ADR-0017：快照优先、派生补空缺）', () => {
  it('有快照时用快照，派生结果被忽略', () => {
    expect(effectiveCity({ id: 1, city: ' 中山 ' }, new Map([[1, '江门']]))).toBe('中山');
  });

  it('无快照时用派生城市；两者都没有时为 undefined', () => {
    expect(effectiveCity({ id: 2, city: null }, new Map([[2, '上海']]))).toBe('上海');
    expect(effectiveCity({ id: 3, city: '  ' }, new Map())).toBeUndefined();
    expect(effectiveCity({ id: 4, city: null })).toBeUndefined();
  });
});
