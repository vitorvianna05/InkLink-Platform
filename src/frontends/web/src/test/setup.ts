import { transferableAbortController } from 'node:util';
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from './server';

// O fetch nativo do Node 24 exige sinais do mesmo runtime; jsdom fornece outra classe.
// Mantém cancelamento real nos testes em vez de remover o signal das consultas.
const nativeController = transferableAbortController();
globalThis.AbortController = nativeController.constructor as typeof AbortController;
globalThis.AbortSignal = nativeController.signal.constructor as typeof AbortSignal;

// jsdom não implementa <dialog>; o polyfill mínimo reproduz abertura/fechamento e o evento close.
if (!HTMLDialogElement.prototype.showModal) {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
}

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
});
afterAll(() => server.close());
