/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_MSAL_CLIENT_ID: string
  readonly VITE_MSAL_AUTHORITY: string
  readonly VITE_API_BASE_URL: string
  readonly VITE_API_SCOPE: string
  readonly VITE_SYNCFUSION_LICENSE_KEY: string
  readonly VITE_APPINSIGHTS_CONNECTION_STRING?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

declare const __APP_VERSION__: string
declare const __APP_COMMIT__: string
declare const __BUILD_TIME__: string
