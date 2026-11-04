// Contratos consumidos pelo website, espelhando os OpenAPI dos serviços (camelCase).
// Fonte oficial: /swagger do Gateway e docs/development/consumer-contracts.md.

export type Uuid = string;
/** Data civil no formato yyyy-MM-dd. */
export type LocalDate = string;
/** Instante ISO 8601 com offset. */
export type Instant = string;

export interface Page<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
  total: number;
  resumeCursor?: string | null;
}

// Identity
export interface Profile {
  id: Uuid;
  displayName: string;
}

// Catalog
export interface ArtistCard {
  id: Uuid;
  name: string;
  studio: string;
  city: string;
  styles: string[];
  startingPrice: number;
}

export interface ArtistProfile extends ArtistCard {
  biography: string;
}

export interface Artwork {
  id: Uuid;
  title: string;
  imageUrl: string;
  alternativeText: string;
  style: string;
  referencePrice: number | null;
}

export type ArtworkKind = 'tattoos' | 'flashes';

export interface Suggestion {
  category: string;
  value: string;
  relevance: number;
}

export interface ArtistInput {
  name: string;
  studio: string;
  city: string;
  biography: string;
  styles: string[];
  startingPrice: number;
}

export interface ArtworkInput {
  title: string;
  imageUrl: string;
  alternativeText: string;
  style: string;
  referencePrice: number | null;
}

// Engagements
export type EngagementState = 'Negotiating' | 'Agreed' | 'Closing' | 'Closed';
export type ProposalState = 'Pending' | 'Superseded' | 'Accepted';

export interface FlashSnapshot {
  id: Uuid;
  title: string;
  imageUrl: string;
  alternativeText: string;
  style: string;
  referencePrice: number | null;
}

export interface CatalogSnapshot {
  artistId: Uuid;
  name: string;
  studio: string;
  city: string;
  startingPrice: number;
  flash: FlashSnapshot | null;
}

export interface Proposal {
  id: Uuid;
  description: string;
  amount: number;
  currency: string;
  state: ProposalState;
  createdAt: Instant;
  acceptedAt: Instant | null;
}

export interface Engagement {
  id: Uuid;
  clientId: Uuid;
  artistAccountId: Uuid;
  idea: string;
  bodyRegion: string;
  approximateSizeCm: number;
  catalog: CatalogSnapshot;
  state: EngagementState;
  createdAt: Instant;
  revision: number;
  currentProposalId: Uuid | null;
  currentProposal: Proposal | null;
}

export interface EngagementRequest {
  artistId: Uuid;
  idea: string;
  bodyRegion: string;
  approximateSizeCm: number;
  flashId?: Uuid | null;
}

export type LifecycleKind = 'Close' | 'Activate';
export type LifecycleOperationStatus = 'Pending' | 'Completed' | 'Rejected' | 'Quarantined';

export interface LifecycleOperation {
  id: Uuid;
  engagementId: Uuid;
  kind: LifecycleKind;
  status: LifecycleOperationStatus;
  attempts: number;
  error: string | null;
  updatedAt: Instant;
}

export interface LifecycleStatus {
  requiresReservation: boolean;
  state: EngagementState;
  operations: LifecycleOperation[];
}

// Messaging
export interface Message {
  id: Uuid;
  conversationId: Uuid;
  authorId: Uuid;
  body: string;
  createdAt: Instant;
  sequence: number;
}

export interface Conversation {
  id: Uuid;
  engagementId: Uuid;
  clientId: Uuid;
  artistAccountId: Uuid;
  writingBlocked: boolean;
  createdAt: Instant;
  lastMessage: Message | null;
  counterpart: { id: Uuid; displayName: string };
  engagementState: EngagementState;
}

// Scheduling
export interface TimeWindow {
  /** HH:mm ou HH:mm:ss, hora local do artista. */
  start: string;
  end: string;
}

export interface Availability {
  artistId: Uuid;
  date: LocalDate;
  timeZoneId: string;
  windows: TimeWindow[];
  busy: { start: Instant; end: Instant }[];
  guaranteesReservation: boolean;
}

export interface Reservation {
  id: Uuid;
  operationId: Uuid;
  engagementId: Uuid;
  clientId: Uuid;
  artistId: Uuid;
  start: Instant;
  end: Instant;
  localDate: LocalDate;
  timeZoneId: string;
  clientName: string;
  artistName: string;
  project: string;
  proposalId: Uuid;
  referenceAmount: number;
  confirmedAt: Instant;
  state: string;
  currency: string;
}

export interface ArtistCalendarContext {
  artistId: Uuid;
  timeZoneId: string;
  observedAt: Instant;
}

export interface SessionCompletion {
  reservationId: Uuid;
  artistId: Uuid;
  clientId: Uuid;
  engagementId: Uuid;
  completedAt: Instant;
  amount: number;
  currency: string;
}

// Finance
export type EntryKind = 'Receipt' | 'Cost';

export interface LedgerEntry {
  id: Uuid;
  artistId: Uuid;
  kind: EntryKind | string;
  description: string;
  amount: number;
  signedAmount: number;
  effectiveDate: LocalDate;
  engagementId: Uuid | null;
  originalEntryId: Uuid | null;
  timeZoneId: string;
  createdAt: Instant;
  currency: string;
}

export interface EntryInput {
  kind: EntryKind;
  description: string;
  amount: number;
  effectiveDate: LocalDate;
  engagementId?: Uuid | null;
}

export interface Dashboard {
  from: LocalDate;
  to: LocalDate;
  timeZoneId: string;
  financial: { received: number; costs: number; estimatedProfit: number; estimatedMargin: number };
  completedSessions: number;
  clientsServed: number;
  averageSessionValue: number;
  scheduledReservations: number;
  scheduledValue: number;
  currentActiveConversations: number;
  scheduleObservedAt: Instant;
  conversationsObservedAt: Instant;
  generatedAt: Instant;
  currency: string;
  conversationBasis: string;
}
