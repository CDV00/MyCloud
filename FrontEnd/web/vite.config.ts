/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Trình duyệt chỉ nói chuyện với Vite (cùng origin); /api được proxy sang Gateway.
// Nhờ vậy cookie refresh token (HttpOnly, SameSite=Strict) hoạt động và không cần CORS.
// Khi chạy qua Aspire, địa chỉ Gateway và cổng được truyền qua biến môi trường.
const gatewayUrl =
  process.env.services__gateway__http__0 ?? process.env.GATEWAY_HTTP ?? 'http://localhost:5000';

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      '/api': { target: gatewayUrl, xfwd: true },
    },
  },
  build: {
    rollupOptions: {
      output: {
        // Thư viện ít đổi tách riêng để trình duyệt cache lâu.
        manualChunks: {
          react: ['react', 'react-dom', 'react-router'],
          mui: ['@mui/material', '@emotion/react', '@emotion/styled'],
          query: ['@tanstack/react-query'],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
  },
});
