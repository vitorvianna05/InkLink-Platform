import { setupServer } from 'msw/node';

/** Servidor de testes: cada teste declara as respostas do backend que exercita. */
export const server = setupServer();
