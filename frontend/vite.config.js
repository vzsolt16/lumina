import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Backend dev target. The app is run with the `http` profile, so it listens on
// http://localhost:5131 only (HTTPS/7204 is not bound, and UseHttpsRedirection
// is a no-op when no HTTPS port is configured). `secure: false` tolerates a
// self-signed cert if you later switch to the `https` profile (7204).
// `/ws` carries SignalR negotiate (HTTP) + WebSocket upgrade, so `ws: true`.
const BACKEND = 'http://localhost:5131'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: BACKEND,
        changeOrigin: true,
        secure: false,
      },
      '/ws': {
        target: BACKEND,
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
})
