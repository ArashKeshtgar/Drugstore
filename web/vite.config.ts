import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The API runs on :5500 (src/PharmaFlow.Api). Proxying keeps the browser on one origin, so the API needs no CORS policy.
export default defineConfig({
  plugins: [react()],
  server: { port: 5501, strictPort: true, proxy: { '/api': { target: 'http://localhost:5500', changeOrigin: false } } },
})
