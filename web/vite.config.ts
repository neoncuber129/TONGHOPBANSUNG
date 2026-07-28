import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: '/TONGHOPBANSUNG/',
  optimizeDeps: {
    exclude: ['sql.js'],
  },
})
