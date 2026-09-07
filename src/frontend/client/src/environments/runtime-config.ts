export interface RuntimeConfig {
  amapKey?: string;
  amapSecurityJsCode?: string;
}

declare global {
  interface Window {
    __TRAVEL_MAP_CONFIG__?: RuntimeConfig;
  }
}

export const runtimeConfig: RuntimeConfig =
  typeof window === 'undefined' ? {} : window.__TRAVEL_MAP_CONFIG__ ?? {};
