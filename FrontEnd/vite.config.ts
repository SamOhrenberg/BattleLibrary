import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Base path is '/' in dev; the Pages subpath is passed via VITE_BASE at build.
export default defineConfig({
  base: process.env.VITE_BASE ?? '/',
  plugins: [react()],
})
