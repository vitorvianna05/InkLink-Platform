import type { Page, Route } from '@playwright/test';

// Respostas sintéticas para checagens visuais/teclado no navegador. Não são fonte de dados
// do produto e não substituem o roteiro com o backend real.

const image = (seed: string) =>
  `data:image/svg+xml;utf8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 500"><rect width="400" height="500" fill="#efe9e4"/><text x="200" y="260" text-anchor="middle" font-family="sans-serif" font-size="28" fill="#85817e">${seed}</text></svg>`)}`;
// Foto de perfil sintética (T10.18): quadrada, como as variantes de 512/128 px.
const photo = (seed: string, color: string) =>
  `data:image/svg+xml;utf8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><rect width="128" height="128" fill="${color}"/><text x="64" y="76" text-anchor="middle" font-family="sans-serif" font-size="40" fill="#fff">${seed}</text></svg>`)}`;
const client = { id: '00000000-0000-7000-8000-00000000c11e', displayName: 'Júlia Vieira', photoUrl: photo('JV', '#7a5c4f'), photoThumbnailUrl: photo('JV', '#7a5c4f') };
const artist = { id: '00000000-0000-7000-8000-0000000a7157', displayName: 'Rafael Moura', photoUrl: photo('RM', '#232222'), photoThumbnailUrl: photo('RM', '#232222') };

const artists = [
  { id: 'a1', name: 'Rafael Moura', studio: 'Moura Tattoo Studio', city: 'São Paulo', styles: ['Blackwork', 'Botânico'], startingPrice: 450, biography: 'Traços que contam histórias.', photoUrl: artist.photoUrl, photoThumbnailUrl: artist.photoThumbnailUrl },
  { id: 'a2', name: 'Camila Azevedo', studio: 'Ateliê Linha Fina', city: 'São Paulo', styles: ['Fineline'], startingPrice: 350, biography: 'A beleza mora nos detalhes.' },
  { id: 'a3', name: 'Bruno Costa', studio: 'Costa Ink', city: 'Rio de Janeiro', styles: ['Realismo'], startingPrice: 600, biography: 'Luz, sombra e personalidade.' },
];

const proposal = { id: 'p1', description: 'Composição botânica exclusiva, desenho incluído.', amount: 450, currency: 'BRL', state: 'Pending', createdAt: '2026-11-01T13:00:00Z', acceptedAt: null };
const engagement = {
  id: 'e1',
  clientId: client.id,
  artistAccountId: artist.id,
  idea: 'Projeto botânico no antebraço, com folhas e flores delicadas',
  bodyRegion: 'Antebraço',
  approximateSizeCm: 12,
  catalog: { artistId: 'a1', name: 'Rafael Moura', studio: 'Moura Tattoo Studio', city: 'São Paulo', startingPrice: 450, flash: null },
  state: 'Negotiating',
  createdAt: '2026-11-01T12:00:00Z',
  revision: 2,
  currentProposalId: 'p1',
  currentProposal: proposal,
};
const conversation = {
  id: 'c1',
  engagementId: 'e1',
  clientId: client.id,
  artistAccountId: artist.id,
  writingBlocked: false,
  createdAt: '2026-11-01T12:00:01Z',
  lastMessage: { id: 'm2', conversationId: 'c1', authorId: artist.id, body: 'Podemos criar uma composição exclusiva.', createdAt: '2026-11-01T12:10:00Z', sequence: 2 },
  counterpart: { id: artist.id, displayName: 'Rafael Moura', photoUrl: artist.photoUrl, photoThumbnailUrl: artist.photoThumbnailUrl },
  engagementState: 'Negotiating',
};
const messages = [
  { id: 'm1', conversationId: 'c1', authorId: client.id, body: 'Oi, Rafael! Quero uma tattoo botânica no antebraço, de uns 12 cm.', createdAt: '2026-11-01T12:05:00Z', sequence: 1 },
  conversation.lastMessage,
];

const page = (items: unknown[]) => ({ items, nextCursor: null, hasMore: false, total: items.length, resumeCursor: 'r' });

export async function mockBackend(pageObject: Page, role: 'anonymous' | 'client' | 'artist' = 'client') {
  const profile = role === 'artist' ? artist : client;
  // Interlocutor conforme a perspectiva, como na Messaging: o artista vê o cliente (com a foto assinada).
  const counterpart = role === 'artist' ? { id: client.id, displayName: client.displayName, photoUrl: client.photoUrl, photoThumbnailUrl: client.photoThumbnailUrl } : conversation.counterpart;
  const seen = { ...conversation, counterpart };
  await pageObject.route((url) => /^\/(bff|api)\//.test(url.pathname), async (route: Route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/bff/session') return role === 'anonymous' ? json({ status: 401, code: 'session.required' }, 401) : json({ authenticated: true, name: profile.displayName });
    if (path === '/bff/antiforgery') return json({ requestToken: 't' });
    if (path === '/api/v1/profiles/me') return json(profile);
    if (path === '/api/v1/scheduling/artist-context') return role === 'artist' ? json({ artistId: artist.id, timeZoneId: 'America/Sao_Paulo', observedAt: new Date().toISOString() }) : json({ status: 403, code: 'authorization.denied' }, 403);
    if (path === '/api/v1/artists' || path === '/api/v1/artists/search') return json(page(artists));
    if (path === '/api/v1/artists/suggestions') {
      const term = (url.searchParams.get('term') ?? '').toLowerCase();
      return json(term.startsWith('sa') ? [{ category: 'city', value: 'São Paulo', relevance: 0.9 }, { category: 'style', value: 'Sakura', relevance: 0.4 }] : []);
    }
    const artistMatch = path.match(/^\/api\/v1\/artists\/(a\d)(\/(tattoos|flashes))?$/);
    if (artistMatch) {
      if (!artistMatch[3]) return json(artists.find((a) => a.id === artistMatch[1]));
      const kind = artistMatch[3];
      // Variantes enviadas (decisão 0018) e uma obra legada por URL externa, que o Estúdio pede para trocar.
      return json(page([1, 2, 3].map((n) => ({ id: `${kind}${n}`, title: `${kind === 'flashes' ? 'Flash' : 'Trabalho'} ${n}`, imageUrl: image(`${kind} ${n}`), thumbnailUrl: image(`${kind} ${n}`), externalImage: n === 3, alternativeText: `Ilustração ${n}`, style: 'Blackwork', referencePrice: kind === 'flashes' ? 400 + n * 50 : null }))));
    }
    if (path === '/api/v1/favorites') return json(page([artists[1]]));
    if (path === '/api/v2/engagements') return json(page([engagement]));
    if (path === '/api/v2/engagements/e1') return json(engagement);
    if (path === '/api/v2/engagements/e1/proposals') return json(page([proposal]));
    if (path === '/api/v2/engagements/e1/lifecycle') return json({ requiresReservation: false, state: 'Negotiating', operations: [] });
    if (path === '/api/v1/conversations') return json(page([seen]));
    if (path === '/api/v1/conversations/c1') return json(seen);
    if (path === '/api/v1/conversations/c1/messages') return json(page(url.searchParams.get('cursor') ? [] : messages));
    if (path === '/api/v1/scheduling/calendar') {
      const r = { id: 'r1', operationId: 'o1', engagementId: 'e1', clientId: client.id, artistId: artist.id, start: '2030-01-15T14:00:00-03:00', end: '2030-01-15T17:00:00-03:00', localDate: '2030-01-15', timeZoneId: 'America/Sao_Paulo', clientName: 'Júlia Vieira', artistName: 'Rafael Moura', project: 'Projeto botânico', proposalId: 'p1', referenceAmount: 450, confirmedAt: '2026-11-02T12:00:00Z', state: 'Confirmed', currency: 'BRL' };
      return json(page([r]));
    }
    if (path.startsWith('/api/v1/scheduling/') && path.includes('/availability/')) return json({ artistId: artist.id, date: path.slice(-10), timeZoneId: 'America/Sao_Paulo', windows: [{ start: '10:00:00', end: '19:00:00' }], busy: [], guaranteesReservation: false });
    if (path === '/api/v1/finance/dashboard') {
      return json({ from: '2026-11-01', to: '2026-12-01', timeZoneId: 'America/Sao_Paulo', financial: { received: 4000, costs: 940, estimatedProfit: 3060, estimatedMargin: 76.5 }, completedSessions: 8, clientsServed: 6, averageSessionValue: 500, scheduledReservations: 3, scheduledValue: 1350, currentActiveConversations: 4, scheduleObservedAt: '2026-11-04T00:00:00Z', conversationsObservedAt: '2026-11-04T00:00:00Z', generatedAt: '2026-11-04T00:00:00Z', currency: 'BRL', conversationBasis: 'Current' });
    }
    if (path === '/api/v1/finance/entries') return json(page([]));
    return json({ status: 404, code: 'not_found' }, 404);
  });
}
