import type { Conversation, Engagement, Message, Proposal, Reservation } from '@/shared/api/contracts';
import { artist, client } from './render';

export const proposal: Proposal = {
  id: 'p1',
  description: 'Composição botânica com desenho exclusivo',
  amount: 450,
  currency: 'BRL',
  state: 'Pending',
  createdAt: '2026-11-01T13:00:00Z',
  acceptedAt: null,
};

export function engagement(overrides: Partial<Engagement> = {}): Engagement {
  return {
    id: 'e1',
    clientId: client.id,
    artistAccountId: artist.id,
    idea: 'Projeto botânico no antebraço',
    bodyRegion: 'Antebraço',
    approximateSizeCm: 12,
    catalog: { artistId: 'a1', name: 'Rafael Moura', studio: 'Moura Tattoo', city: 'São Paulo', startingPrice: 450, flash: null },
    state: 'Negotiating',
    createdAt: '2026-11-01T12:00:00Z',
    revision: 2,
    currentProposalId: proposal.id,
    currentProposal: proposal,
    ...overrides,
  };
}

export function conversation(overrides: Partial<Conversation> = {}): Conversation {
  return {
    id: 'c1',
    engagementId: 'e1',
    clientId: client.id,
    artistAccountId: artist.id,
    writingBlocked: false,
    createdAt: '2026-11-01T12:00:01Z',
    lastMessage: null,
    counterpart: { id: artist.id, displayName: 'Rafael Moura' },
    engagementState: 'Negotiating',
    ...overrides,
  };
}

export function message(sequence: number, authorId: string, body: string): Message {
  return { id: `m${sequence}`, conversationId: 'c1', authorId, body, createdAt: `2026-11-01T12:0${sequence}:00Z`, sequence };
}

export function reservation(overrides: Partial<Reservation> = {}): Reservation {
  return {
    id: 'r1',
    operationId: 'op1',
    engagementId: 'e1',
    clientId: client.id,
    artistId: artist.id,
    start: '2030-01-15T14:00:00-03:00',
    end: '2030-01-15T17:00:00-03:00',
    localDate: '2030-01-15',
    timeZoneId: 'America/Sao_Paulo',
    clientName: 'Júlia Vieira',
    artistName: 'Rafael Moura',
    project: 'Projeto botânico',
    proposalId: 'p1',
    referenceAmount: 450,
    confirmedAt: '2026-11-02T12:00:00Z',
    state: 'Confirmed',
    currency: 'BRL',
    ...overrides,
  };
}
