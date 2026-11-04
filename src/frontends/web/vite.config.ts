/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs';
import { Agent } from 'node:https';
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
  const proxy: ProxyOptions = {
    target: bff,
    changeOrigin: true,
    secure: env.INKLINK_BFF_INSECURE !== 'true',
    // Confiança restrita ao upstream configurado, sem desabilitar a verificação TLS.
    agent: env.INKLINK_BFF_CA_CERT ? new Agent({ ca: readFileSync(env.INKLINK_BFF_CA_CERT) }) : undefined,
  };
  // Imagens (decisão 0018): /media/<bucket>/<chave> é encaminhado ao armazenamento S3 local (SeaweedFS),
  // mantendo o caminho assinado e o host do endpoint para que endereços assinados continuem válidos.
  // Cookies e credenciais do website nunca seguem para o armazenamento.
  const media: ProxyOptions = {
    target: env.INKLINK_MEDIA_URL ?? 'http://127.0.0.1:5188',
    changeOrigin: true,
    rewrite: (path) => path.replace(/^\/media/, ''),
    configure: (server) => {
      server.on('proxyReq', (request) => {
        request.removeHeader('cookie');
        request.removeHeader('authorization');
      });
    },
  };
  const routes = { '/bff': proxy, '/api': proxy, '/signin-oidc': proxy, '/signout-callback-oidc': proxy, '/media': media };
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
      proxy: routes,
    },
    preview: { port: Number(env.INKLINK_WEB_PORT ?? 5173), strictPort: true, https, proxy: routes },
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
