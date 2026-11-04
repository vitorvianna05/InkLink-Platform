import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { conversation, engagement, reservation } from '@/test/fixtures';
import { artist, page, problem, renderApp, signedIn } from '@/test/render';
import { server } from '@/test/server';

function backend() {
  server.use(
    http.get('/api/v1/scheduling/calendar', () => HttpResponse.json(page([reservation()]))),
    http.get('/api/v1/scheduling/availability/:date', ({ params }) =>
      HttpResponse.json({ artistId: artist.id, date: params.date, timeZoneId: 'America/Sao_Paulo', windows: [{ start: '10:00:00', end: '19:00:00' }], busy: [], guaranteesReservation: false }),
    ),
    http.get('/api/v1/scheduling/engagements/e1/availability/:date', ({ params }) =>
      HttpResponse.json({ artistId: artist.id, date: params.date, timeZoneId: 'America/Sao_Paulo', windows: [{ start: '10:00:00', end: '19:00:00' }], busy: [], guaranteesReservation: false }),
    ),
    http.get('/api/v2/engagements', () => HttpResponse.json(page([engagement({ state: 'Agreed' })]))),
    http.get('/api/v1/conversations', () => HttpResponse.json(page([conversation()]))),
  );
}

describe('agenda do artista', () => {
  it('sinaliza dias com reservas e lista horários do dia com cliente e projeto', async () => {
    signedIn(artist, true);
    backend();
    renderApp('/estudio/agenda?dia=2030-01-15');
    expect(await screen.findByRole('heading', { name: /janeiro de 2030/i })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /15 de janeiro de 2030, com reservas/ })).toHaveAttribute('aria-pressed', 'true');
    expect(await screen.findByText(/Júlia Vieira/)).toBeInTheDocument();
    expect(screen.getByText(/Projeto botânico · referência/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Conversa com Júlia Vieira' })).toHaveAttribute('href', '/estudio/mensagens/c1');
  });

  it('conflito de reserva é informado sem ocupar o horário', async () => {
    signedIn(artist, true);
    backend();
    const bodies: unknown[] = [];
    const keys: string[] = [];
    server.use(
      http.post('/api/v1/scheduling/reservations', async ({ request }) => {
        bodies.push(await request.json());
        keys.push(request.headers.get('Idempotency-Key') ?? '');
        return problem(409, 'scheduling.conflict');
      }),
    );
    renderApp('/estudio/agenda?dia=2030-01-16&atendimento=e1');
    const form = (await screen.findByRole('heading', { name: 'Confirmar reserva' })).closest('form')!;
    await userEvent.type(await within(form).findByLabelText('Início'), '15:00');
    await userEvent.type(within(form).getByLabelText('Término'), '14:00');
    await userEvent.click(within(form).getByRole('button', { name: 'Confirmar reserva' }));
    expect(within(form).getByText('O término deve ser posterior ao início.')).toBeInTheDocument();
    expect(bodies).toHaveLength(0);

    await userEvent.clear(within(form).getByLabelText('Término'));
    await userEvent.type(within(form).getByLabelText('Término'), '17:00');
    await userEvent.click(within(form).getByRole('button', { name: 'Confirmar reserva' }));
    expect(await within(form).findByText(/conflita com outra reserva/)).toBeInTheDocument();
    expect(bodies[0]).toEqual({ engagementId: 'e1', start: '2030-01-16T15:00:00-03:00', end: '2030-01-16T17:00:00-03:00' });
    expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
  });
  it('envia disponibilidade com segundos conforme o contrato TimeOnly da API', async () => {
    signedIn(artist, true);
    backend();
    let sent: unknown;
    server.use(http.put('/api/v1/scheduling/availability/:date', async ({ request, params }) => {
      sent = await request.json();
      return HttpResponse.json({ artistId: artist.id, date: params.date, timeZoneId: 'America/Sao_Paulo', windows: [], busy: [], guaranteesReservation: false });
    }));
    renderApp('/estudio/agenda?dia=2030-01-16');
    const form = (await screen.findByRole('heading', { name: 'Disponibilidade do dia' })).closest('form')!;
    await userEvent.clear(within(form).getByLabelText('Início'));
    await userEvent.type(within(form).getByLabelText('Início'), '11:00');
    await userEvent.click(within(form).getByRole('button', { name: 'Salvar disponibilidade' }));
    expect(await within(form).findByText('Disponibilidade salva.')).toBeInTheDocument();
    expect(sent).toEqual({ timeZoneId: 'America/Sao_Paulo', windows: [{ start: '11:00:00', end: '19:00:00' }] });
  });

});
