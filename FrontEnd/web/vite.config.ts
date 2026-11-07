/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Trình duyệt chỉ nói chuyện với Vite (cùng origin); /api được proxy sang Gateway.
// Nhờ vậy cookie refresh token (HttpOnly, SameSite=Strict) hoạt động và không cần CORS.
// Khi chạy qua Aspire, địa chỉ Gateway và cổng được truyền qua biến môi trường.
const gatewayUrl =
  process.env.services__gateway__http__0 ?? process.env.GATEWAY_HTTP ?? 'http://localhost:5000';
// Object storage (MinIO API). Presigned URL được ký cho chính origin của trình duyệt, nên Vite phải chuyển nguyên
// Host header (changeOrigin: false) để chữ ký S3 khớp (ADR-013).
const objectStorageUrl = process.env.MINIO_URL ?? 'http://localhost:9000';

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      // ws: SignalR (thông báo realtime) đi qua cùng proxy.
      '/api': { target: gatewayUrl, xfwd: true, ws: true },
      '/mycloud-blobs': {
        target: objectStorageUrl,
        changeOrigin: false,
        // File người dùng cùng origin với app: mở trực tiếp file HTML/SVG cũng chỉ chạy trong sandbox (ADR-015).
        configure: (proxy) =>
          proxy.on('proxyRes', (res) => {
            res.headers['content-security-policy'] = 'sandbox';
            res.headers['x-content-type-options'] = 'nosniff';
          }),
      },
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
