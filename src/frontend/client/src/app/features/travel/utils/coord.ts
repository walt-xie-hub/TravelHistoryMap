/**
 * 坐标系转换：WGS-84（库表存储基准，见 docs/adr/0003）↔ GCJ-02（高德地图渲染坐标系）。
 * 采用公开的近似算法（火星坐标系加密偏移），中国大陆区域外不做转换。
 * 注意：GCJ-02 与 BD-09（百度）不是同一坐标系；如将来接入百度地图需另加 BD-09 转换。
 */

const SEMI_MAJOR = 6378245.0;
const EARTH_ECCENTRICITY_SQ = 0.00669342162296594323;

function isOutOfChina(lng: number, lat: number): boolean {
  return !(lng > 73.66 && lng < 135.05 && lat > 3.86 && lat < 53.55);
}

function transformLat(lng: number, lat: number): number {
  let ret =
    -100.0 +
    2.0 * lng +
    3.0 * lat +
    0.2 * lat * lat +
    0.1 * lng * lat +
    0.2 * Math.sqrt(Math.abs(lng));
  ret += ((20.0 * Math.sin(6.0 * lng * Math.PI) + 20.0 * Math.sin(2.0 * lng * Math.PI)) * 2.0) / 3.0;
  ret += ((20.0 * Math.sin(lat * Math.PI) + 40.0 * Math.sin((lat / 3.0) * Math.PI)) * 2.0) / 3.0;
  ret +=
    ((160.0 * Math.sin((lat / 12.0) * Math.PI) + 320 * Math.sin((lat * Math.PI) / 30.0)) * 2.0) /
    3.0;
  return ret;
}

function transformLng(lng: number, lat: number): number {
  let ret =
    300.0 +
    lng +
    2.0 * lat +
    0.1 * lng * lng +
    0.1 * lng * lat +
    0.1 * Math.sqrt(Math.abs(lng));
  ret += ((20.0 * Math.sin(6.0 * lng * Math.PI) + 20.0 * Math.sin(2.0 * lng * Math.PI)) * 2.0) / 3.0;
  ret += ((20.0 * Math.sin(lng * Math.PI) + 40.0 * Math.sin((lng / 3.0) * Math.PI)) * 2.0) / 3.0;
  ret +=
    ((150.0 * Math.sin((lng / 12.0) * Math.PI) + 300.0 * Math.sin((lng / 30.0) * Math.PI)) * 2.0) /
    3.0;
  return ret;
}

/** WGS-84 → GCJ-02（高德使用）。返回 [经度, 纬度]。 */
export function wgs84ToGcj02(lng: number, lat: number): readonly [number, number] {
  if (isOutOfChina(lng, lat)) return [lng, lat];

  let dLat = transformLat(lng - 105.0, lat - 35.0);
  let dLng = transformLng(lng - 105.0, lat - 35.0);
  const radLat = (lat / 180.0) * Math.PI;
  let magic = Math.sin(radLat);
  magic = 1 - EARTH_ECCENTRICITY_SQ * magic * magic;
  const sqrtMagic = Math.sqrt(magic);
  dLat = (dLat * 180.0) / (((SEMI_MAJOR * (1 - EARTH_ECCENTRICITY_SQ)) / (magic * sqrtMagic)) * Math.PI);
  dLng = (dLng * 180.0) / ((SEMI_MAJOR / sqrtMagic) * Math.cos(radLat) * Math.PI);

  return [lng + dLng, lat + dLat];
}

/** GCJ-02 → WGS-84（近似逆变换，用于将来从高德地图取点录入时转回存储基准）。 */
export function gcj02ToWgs84(lng: number, lat: number): readonly [number, number] {
  const [convertedLng, convertedLat] = wgs84ToGcj02(lng, lat);
  return [lng * 2 - convertedLng, lat * 2 - convertedLat];
}
