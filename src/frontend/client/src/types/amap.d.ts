/**
 * 高德地图 JS API 2.0 的最小全局类型声明（按需裁剪，避免引入非官方 @types）。
 * SDK 由 AmapLoaderService 运行时注入 script 后挂到 window.AMap。
 */

/** 经纬度输入：高德接受 [经度, 纬度] 二元组 */
export type AmapLngLat = [number, number];

export interface AmapPixel {
  x: number;
  y: number;
}

export interface AmapMapOptions {
  viewMode?: '2D' | '3D';
  zoom?: number;
  center?: AmapLngLat;
}

export interface AmapMarkerOptions {
  position: AmapLngLat;
  content?: string | HTMLElement;
  offset?: AmapPixel;
  zIndex?: number;
  title?: string;
}

export interface AmapInfoWindowOptions {
  content?: string | HTMLElement;
  offset?: AmapPixel;
  isCustom?: boolean;
  closeWhenClickMap?: boolean;
}

export interface AmapClickEvent {
  lnglat: AmapLngLat;
  target: unknown;
}

export interface AmapMap {
  add(overlay: unknown): void;
  remove(overlay: unknown): void;
  setFitView(overlays?: readonly unknown[], immediately?: boolean, avoid?: readonly number[]): void;
  setZoomAndCenter(zoom: number, center: AmapLngLat): void;
  setZoom(zoom: number): void;
  setCenter(center: AmapLngLat): void;
  getZoom(): number;
  destroy(): void;
}

export interface AmapMarker {
  on(event: 'click', handler: (e: AmapClickEvent) => void): void;
  getPosition(): AmapLngLat;
}

export interface AmapInfoWindow {
  open(map: AmapMap, lnglat: AmapLngLat): void;
  close(): void;
  setContent(content: string | HTMLElement): void;
}

/** window.AMap 命名空间的构造器集合 */
export interface AmapNamespace {
  Map: new (container: string | HTMLElement, options?: AmapMapOptions) => AmapMap;
  Marker: new (options: AmapMarkerOptions) => AmapMarker;
  InfoWindow: new (options: AmapInfoWindowOptions) => AmapInfoWindow;
  Pixel: new (x: number, y: number) => AmapPixel;
}

declare global {
  interface Window {
    AMap?: AmapNamespace;
    /** 高德 JS API 2.0 安全密钥（须在引入 SDK 前设置） */
    _AMapSecurityConfig?: { securityJsCode?: string };
  }
}

export {};
