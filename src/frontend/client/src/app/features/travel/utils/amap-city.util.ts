/**
 * AMap 返回结果 → 城市短名的提取（ADR-0015 / ADR-0017）。
 *
 * 写入记录时用它落 City 快照；地图渲染无 City 的旧记录时用它**派生**城市（只用于分组，不落库）。
 */

/** 去掉行政后缀（上海市→上海、延边朝鲜族自治州→延边朝鲜族、地区/盟），让城市名短且可比较 */
export function stripAdminSuffix(name: string): string {
  return name.replace(/(自治州|地区|盟)$/, '').replace(/市$/, '');
}

/** 只接受非空字符串（其余类型/空白一律视为空） */
function firstString(value: unknown): string {
  return typeof value === 'string' && value.trim() ? value.trim() : '';
}

/** 从 AMap 搜索候选提取城市：cityname ?? pname（ADR-0015）。字段不在 SDK 类型定义里，故取 unknown 再窄化。 */
export function cityFromPlace(place: unknown): string {
  const extended = (place ?? {}) as { cityname?: unknown; pname?: unknown };
  const raw = firstString(extended.cityname) || firstString(extended.pname);
  return raw ? stripAdminSuffix(raw) : '';
}

/** 从逆地理 addressComponent 提取城市：city ?? province（city 可能是数组，如直辖市）。 */
export function cityFromComponents(comp?: { province?: string; city?: string | string[] }): string {
  const cityValue = comp?.city;
  const city = typeof cityValue === 'string' ? cityValue : Array.isArray(cityValue) ? (cityValue[0] ?? '') : '';
  const raw = firstString(city) || firstString(comp?.province);
  return raw ? stripAdminSuffix(raw) : '';
}
