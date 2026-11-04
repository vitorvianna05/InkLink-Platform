import { ArrowLeft, ArrowUpRight, MapPin, MessageCircle } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router';
import { useEngagements } from '@/features/engagements/api';
import { RequestDialog } from '@/features/engagements/RequestDialog';
import { useConversations } from '@/features/messaging/api';
import { useSession } from '@/features/session/session';
import type { Artwork, ArtworkKind } from '@/shared/api/contracts';
import { engagementStateLabel, money } from '@/shared/lib/format';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { Tabs } from '@/shared/ui/Tabs';
import { useArtist, useArtworks } from './api';
import { FavoriteButton } from './ArtistCardView';

function ArtworkList({ artistId, kind, onWant }: { artistId: string; kind: ArtworkKind; onWant?: (flash: Artwork) => void }) {
  const works = useArtworks(artistId, kind);
  if (works.isPending) return <Loading label="Carregando trabalhos…" />;
  if (works.isError) return <ErrorState error={works.error} onRetry={() => void works.refetch()} />;
  const items = works.data.pages.flatMap((page) => page.items);
  if (items.length === 0) {
    return <EmptyState title={kind === 'flashes' ? 'Este artista ainda não publicou flashes.' : 'Este artista ainda não publicou tatuagens realizadas.'} />;
  }
  return (
    <>
      <div className="work-grid">
        {items.map((work) => (
          <figure key={work.id}>
            <img src={work.imageUrl} alt={work.alternativeText} loading="lazy" />
            <figcaption>
              <strong>{work.title}</strong>
              <span>
                {work.style}
                {work.referencePrice !== null && ` · referência ${money(work.referencePrice)}`}
              </span>
              {onWant && (
                <button type="button" className="text-button red" onClick={() => onWant(work)}>
                  Quero este flash <ArrowUpRight size={14} aria-hidden="true" />
                </button>
              )}
            </figcaption>
          </figure>
        ))}
      </div>
      {works.hasNextPage && (
        <div className="row" style={{ justifyContent: 'center', marginTop: 16 }}>
          <button type="button" className="button outline" onClick={() => void works.fetchNextPage()} disabled={works.isFetchingNextPage}>
            Carregar mais
          </button>
        </div>
      )}
    </>
  );
}

/** Conversas do cliente com este artista, incluindo atendimentos encerrados (somente consulta). */
function useArtistConversations(artistId: string, enabled: boolean) {
  const engagements = useEngagements('client', enabled);
  const conversations = useConversations(enabled);
  return useMemo(() => {
    const ids = new Set((engagements.data ?? []).filter((e) => e.catalog.artistId === artistId).map((e) => e.id));
    return (conversations.data ?? []).filter((c) => ids.has(c.engagementId));
  }, [engagements.data, conversations.data, artistId]);
}

export function ArtistPage() {
  const { artistId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const { session, signIn } = useSession();
  const artist = useArtist(artistId);
  const conversations = useArtistConversations(artistId, session !== null);
  const [requestOpen, setRequestOpen] = useState(false);
  const [flash, setFlash] = useState<Artwork | null>(null);
  const from = (location.state as { from?: string } | null)?.from;

  const tabs = [
    { id: 'tatuagens', label: 'Tatuagens realizadas' },
    { id: 'flashes', label: 'Flashes' },
    ...(conversations.length ? [{ id: 'conversa', label: <><MessageCircle size={14} aria-hidden="true" /> Conversa</> }] : []),
  ];
  const requested = params.get('aba') ?? 'tatuagens';
  const tab = tabs.some((t) => t.id === requested) ? requested : 'tatuagens';

  function openRequest(selected: Artwork | null) {
    if (!session) {
      signIn();
      return;
    }
    setFlash(selected);
    setRequestOpen(true);
  }

  const back = (
    <button type="button" className="text-button" onClick={() => (from ? navigate(-1) : navigate('/explorar'))}>
      <ArrowLeft size={16} aria-hidden="true" /> {from ? 'Voltar' : 'Explorar artistas'}
    </button>
  );

  if (artist.isPending) return <Loading label="Carregando perfil…" />;
  if (artist.isError) {
    return (
      <>
        {back}
        <ErrorState error={artist.error} onRetry={() => void artist.refetch()}>
          <Link className="button outline" to="/explorar">
            Explorar artistas
          </Link>
        </ErrorState>
      </>
    );
  }

  const profile = artist.data;
  return (
    <article>
      {back}
      <div className="profile-header" style={{ marginTop: 16 }}>
        <div>
          <span className="eyebrow">{profile.studio}</span>
          <h1 style={{ fontSize: 'clamp(32px, 5vw, 56px)' }}>{profile.name}</h1>
          <p className="location">
            <MapPin size={14} aria-hidden="true" /> {profile.city}
          </p>
          <p>{profile.biography}</p>
          <div className="pill-row">
            {profile.styles.map((style) => (
              <span key={style} className="pill">
                {style}
              </span>
            ))}
          </div>
        </div>
        <aside className="profile-card" aria-label="Solicitar orçamento">
          <small className="muted">Sessões a partir de</small>
          <span className="price">{money(profile.startingPrice)}</span>
          <small className="muted">Preço inicial de referência. O valor final vem da proposta do artista.</small>
          <div className="row">
            <button type="button" className="button red" style={{ flex: 1 }} onClick={() => openRequest(null)}>
              Contar minha ideia <ArrowUpRight size={16} aria-hidden="true" />
            </button>
            <FavoriteButton artistId={profile.id} artistName={profile.name} />
          </div>
        </aside>
      </div>

      <Tabs label="Trabalhos e conversa do artista" tabs={tabs} selected={tab} onSelect={(id) => setParams({ aba: id }, { replace: true, state: location.state })}>
        {tab === 'tatuagens' && <ArtworkList artistId={profile.id} kind="tattoos" />}
        {tab === 'flashes' && (
          <>
            <p className="muted">Flashes são desenhos criados pelo artista. Tamanho, valor e adaptações são combinados na negociação.</p>
            <ArtworkList artistId={profile.id} kind="flashes" onWant={openRequest} />
          </>
        )}
        {tab === 'conversa' && (
          <ul className="list">
            {conversations.map((conversation) => (
              <li key={conversation.id}>
                <Link className="list-item" to={`/mensagens/${conversation.id}`}>
                  <div className="grow">
                    <h3>Conversa com {profile.name}</h3>
                    <p>{conversation.lastMessage?.body ?? 'Nenhuma mensagem ainda.'}</p>
                  </div>
                  <span className={conversation.writingBlocked || conversation.engagementState === 'Closed' ? 'status red' : 'status green'}>
                    {engagementStateLabel(conversation.engagementState)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Tabs>

      <RequestDialog open={requestOpen} onClose={() => setRequestOpen(false)} artistId={profile.id} artistName={profile.name} flash={flash} />
    </article>
  );
}
