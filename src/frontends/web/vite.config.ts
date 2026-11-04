/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv, type ProxyOptions } from 'vite';

// O website conversa somente com o BFF. Em desenvolvimento, o Vite atende na mesma
// máquina (localhost) e encaminha /bff, /api e os retornos OIDC ao BFF configurado.
// Os cookies __Host- do BFF exigem HTTPS; por isso o servidor local usa o certificado
// de desenvolvimento confiável indicado por INKLINK_DEV_CERT/INKLINK_DEV_KEY.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'INKLINK_');
  const bff = env.INKLINK_BFF_URL ?? 'https://localhost:5192';
  const proxy: ProxyOptions = { target: bff, changeOrigin: true, secure: env.INKLINK_BFF_INSECURE !== 'true' };
  const https = env.INKLINK_DEV_CERT && env.INKLINK_DEV_KEY
    ? { cert: readFileSync(env.INKLINK_DEV_CERT), key: readFileSync(env.INKLINK_DEV_KEY) }
    : undefined;

  return {
    plugins: [react()],
    resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
    server: {
      port: Number(env.INKLINK_WEB_PORT ?? 5173),
      strictPort: true,
      https,
      proxy: { '/bff': proxy, '/api': proxy, '/signin-oidc': proxy, '/signout-callback-oidc': proxy },
    },
    preview: { port: Number(env.INKLINK_WEB_PORT ?? 5173), strictPort: true, https, proxy: { '/bff': proxy, '/api': proxy } },
    build: { sourcemap: true, target: 'es2022' },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      css: false,
      restoreMocks: true,
    },
  };
});
