import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Message } from '@/shared/api/contracts';
import { conversation, message } from '@/test/fixtures';
import { artist, client, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

describe('conversas', () => {
  it('lista e leitura são páginas distintas e a listagem identifica o artista', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations', () =>
        HttpResponse.json(page([conversation({ lastMessage: message(1, client.id, 'Separei referências') }), conversation({ id: 'c2', engagementId: 'e2', engagementState: 'Closed', writingBlocked: true, counterpart: { id: 'x', displayName: 'Bruno Costa' } })])),
      ),
    );
    renderApp('/mensagens');
    const list = await screen.findByRole('list');
    expect(within(list).getByText('Rafael Moura')).toBeInTheDocument();
    expect(within(list).getByText('Separei referências')).toBeInTheDocument();
    expect(within(list).getByText('Serviço encerrado')).toBeInTheDocument();
    expect(screen.queryByRole('log')).not.toBeInTheDocument();
  });

  it('abre pelo endereço, distingue autoria e envia com chave idempotente', async () => {
    signedIn(client);
    const history: Message[] = [message(1, client.id, 'Oi, Rafael!'), message(2, artist.id, 'Oi, Júlia! Adorei a ideia.')];
    const keys: string[] = [];
    server.use(
      http.get('/api/v1/conversations/c1', () => HttpResponse.json(conversation())),
      http.get('/api/v1/conversations/c1/messages', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        return HttpResponse.json(page(cursor ? history.slice(2) : history, { resumeCursor: 'resume' }));
      }),
      http.post('/api/v1/conversations/c1/messages', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key') ?? '');
        const body = (await request.json()) as { body: string };
        const created = message(3, client.id, body.body);
        history.push(created);
        return HttpResponse.json(created, { status: 201 });
      }),
      http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation()]))),
    );
    renderApp('/mensagens/c1');
    expect(await screen.findByRole('heading', { name: 'Rafael Moura' })).toBeInTheDocument();
    const log = await screen.findByRole('log');
    expect(await within(log).findByText('Oi, Rafael!')).toBeInTheDocument();
    expect(within(log).getByText('Oi, Rafael!').closest('.bubble')).toHaveClass('mine');
    expect(within(log).getByText('Oi, Júlia! Adorei a ideia.').closest('.bubble')).not.toHaveClass('mine');

    const input = screen.getByLabelText('Mensagem');
    const send = screen.getByRole('button', { name: 'Enviar mensagem' });
    await userEvent.type(input, '   ');
    expect(send).toBeDisabled();
    await userEvent.clear(input);
    await userEvent.type(input, 'Sábado de manhã funciona?');
    await userEvent.click(send);
    expect(await within(log).findByText('Sábado de manhã funciona?')).toBeInTheDocument();
    expect(keys).toHaveLength(1);
    expect(input).toHaveValue('');
  });

  it('conversa encerrada mantém histórico e bloqueia o envio', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations/c1', () => HttpResponse.json(conversation({ writingBlocked: true, engagementState: 'Closed' }))),
      http.get('/api/v1/conversations/c1/messages', () => HttpResponse.json(page([message(1, client.id, 'Obrigada!')]))),
    );
    renderApp('/mensagens/c1');
    expect(await screen.findByText('Obrigada!')).toBeInTheDocument();
    expect(screen.getAllByText('Serviço encerrado').length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Mensagem')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Enviar mensagem' })).toBeDisabled();
  });

  it('identificador inexistente ou de terceiro não expõe conteúdo e oferece retorno', async () => {
    signedIn(client);
    server.use(
      http.get('/api/v1/conversations/zz', () => problem(404, 'messaging.not_found')),
      http.get('/api/v1/conversations/zz/messages', () => problem(404, 'messaging.not_found')),
    );
    renderApp('/mensagens/zz');
    expect(await screen.findByText('Não encontramos este conteúdo.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver minhas conversas' })).toBeInTheDocument();
  });
});
