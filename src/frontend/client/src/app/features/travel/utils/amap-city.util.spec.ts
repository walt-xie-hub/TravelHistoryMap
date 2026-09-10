import { cityFromComponents, cityFromPlace, stripAdminSuffix } from './amap-city.util';

describe('stripAdminSuffix', () => {
  it('只剥市 / 自治州 / 地区 / 盟（ADR-0015 的既有约定：省名保留“省”，不误剥）', () => {
    expect(stripAdminSuffix('上海市')).toBe('上海');
    expect(stripAdminSuffix('中山市')).toBe('中山');
    expect(stripAdminSuffix('延边朝鲜族自治州')).toBe('延边朝鲜族');
    expect(stripAdminSuffix('阿里地区')).toBe('阿里');
    expect(stripAdminSuffix('锡林郭勒盟')).toBe('锡林郭勒');
    expect(stripAdminSuffix('牡丹江市')).toBe('牡丹江');
    expect(stripAdminSuffix('广东省')).toBe('广东省');
  });
});

describe('cityFromComponents（AMap 逆地理 addressComponent）', () => {
  it('优先取 city，缺失时回退 province', () => {
    expect(cityFromComponents({ city: '中山市', province: '广东省' })).toBe('中山');
    expect(cityFromComponents({ province: '广东省' })).toBe('广东省');
    expect(cityFromComponents({ province: '四川' })).toBe('四川');
  });

  it('city 为数组时取第一项（直辖市/特殊返回）', () => {
    expect(cityFromComponents({ city: ['上海市', '上海'], province: '上海市' })).toBe('上海');
    expect(cityFromComponents({ city: [] as string[], province: '北京市' })).toBe('北京');
  });

  it('空 / 空白 / undefined 返回空串', () => {
    expect(cityFromComponents(undefined)).toBe('');
    expect(cityFromComponents({ city: '   ', province: '' })).toBe('');
  });
});

describe('cityFromPlace（AMap 搜索候选）', () => {
  it('优先取 cityname，缺失时回退 pname，并剥后缀', () => {
    expect(cityFromPlace({ cityname: '中山市', pname: '广东省' })).toBe('中山');
    expect(cityFromPlace({ pname: '浙江省' })).toBe('浙江省');
    expect(cityFromPlace({})).toBe('');
  });
});
