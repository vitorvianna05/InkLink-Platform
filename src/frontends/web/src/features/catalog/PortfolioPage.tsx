import { ArrowUpRight, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { useEngagements } from '@/features/engagements/api';
import { parseAmount } from '@/features/engagements/ProposalSection';
import { RequireArtist } from '@/features/session/guards';
import type { Session } from '@/features/session/session';
import type { ArtistProfile, Artwork, ArtworkKind } from '@/shared/api/contracts';
import { ApiError } from '@/shared/api/http';
import { money } from '@/shared/lib/format';
import { Dialog } from '@/shared/ui/Dialog';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { describeError, EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useArtist, useArtworks, useDeleteArtwork, usePublishArtist, useSaveArtwork } from './api';

// O Catálogo não oferece GET /artists/me. O identificador público do próprio perfil vem da
// resposta da publicação ou do snapshot dos atendimentos; o navegador guarda apenas essa
// referência como conveniência, nunca os dados do perfil.
const storageKey = (profileId: string) => `inklink.catalog-artist.${profileId}`;

function readStoredId(profileId: string): string | null {
  try {
    return window.localStorage.getItem(storageKey(profileId));
  } catch {
    return null;
  }
}

function storeId(profileId: string, artistId: string) {
  try {
    window.localStorage.setItem(storageKey(profileId), artistId);
  } catch {
    // Armazenamento indisponível: o identificador volta a ser obtido na próxima publicação.
  }
}

function ProfileForm({ initial, onPublished }: { initial: ArtistProfile | null; onPublished: (profile: ArtistProfile) => void }) {
  const publish = usePublishArtist();
  const [name, setName] = useState(initial?.name ?? '');
  const [studio, setStudio] = useState(initial?.studio ?? '');
  const [city, setCity] = useState(initial?.city ?? '');
  const [biography, setBiography] = useState(initial?.biography ?? '');
  const [styles, setStyles] = useState(initial?.styles.join(', ') ?? '');
  const [price, setPrice] = useState(initial ? String(initial.startingPrice).replace('.', ',') : '');
  const [error, setError] = useState<string | null>(null);

  return (
    <form
      className="form-card"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        const startingPrice = parseAmount(price);
        const styleList = styles.split(',').map((s) => s.trim()).filter(Boolean);
        if (!name.trim() || !studio.trim() || !city.trim() || !biography.trim()) return setError('Preencha nome, estúdio, cidade e biografia.');
        if (styleList.length === 0) return setError('Informe ao menos um estilo.');
        if (startingPrice === null) return setError('Informe o preço inicial de referência.');
        publish.mutate(
          { name: name.trim(), studio: studio.trim(), city: city.trim(), biography: biography.trim(), styles: styleList, startingPrice },
          { onSuccess: onPublished },
        );
      }}
    >
      <h3>{initial ? 'Editar perfil público' : 'Publicar perfil público'}</h3>
      <p className="muted">A publicação é imediata no catálogo.</p>
      <div className="form-grid">
        <label className="field">
          <span>Nome público</span>
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label className="field">
          <span>Estúdio</span>
          <input value={studio} onChange={(e) => setStudio(e.target.value)} />
        </label>
        <label className="field">
          <span>Cidade</span>
          <input value={city} onChange={(e) => setCity(e.target.value)} />
        </label>
        <label className="field">
          <span>Preço inicial (R$)</span>
          <input value={price} inputMode="decimal" onChange={(e) => setPrice(e.target.value)} />
        </label>
      </div>
      <label className="field">
        <span>Estilos (separados por vírgula)</span>
        <input value={styles} onChange={(e) => setStyles(e.target.value)} placeholder="Blackwork, Fineline" />
      </label>
      <label className="field">
        <span>Biografia</span>
        <textarea value={biography} onChange={(e) => setBiography(e.target.value)} />
      </label>
      {(error || publish.isError) && (
        <p className="notice error" role="alert">
          {error ?? (publish.error instanceof ApiError && Object.values(publish.error.fieldErrors)[0]?.[0]) ?? describeError(publish.error).title}
        </p>
      )}
      {publish.isSuccess && (
        <p className="notice success" role="status">
          Perfil publicado.
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button red" disabled={publish.isPending}>
          {publish.isPending ? 'Publicando…' : 'Salvar perfil'}
        </button>
      </div>
    </form>
  );
}

const kindLabels: Record<ArtworkKind, { title: string; singular: string }> = {
  tattoos: { title: 'Tatuagens realizadas', singular: 'tatuagem' },
  flashes: { title: 'Flashes', singular: 'flash' },
};

function ArtworkDialog({ kind, artwork, open, onClose }: { kind: ArtworkKind; artwork: Artwork | null; open: boolean; onClose: () => void }) {
  const save = useSaveArtwork(kind);
  const [title, setTitle] = useState(artwork?.title ?? '');
  const [imageUrl, setImageUrl] = useState(artwork?.imageUrl ?? '');
  const [alt, setAlt] = useState(artwork?.alternativeText ?? '');
  const [style, setStyle] = useState(artwork?.style ?? '');
  const [price, setPrice] = useState(artwork?.referencePrice != null ? String(artwork.referencePrice).replace('.', ',') : '');
  const [error, setError] = useState<string | null>(null);

  return (
    <Dialog open={open} onClose={onClose} title={artwork ? `Editar ${kindLabels[kind].singular}` : `Nova ${kindLabels[kind].singular}`} eyebrow="PORTFÓLIO">
      <form
        className="stack"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          setError(null);
          if (!title.trim() || !alt.trim() || !style.trim()) return setError('Preencha título, estilo e texto alternativo.');
          if (!/^https:\/\//i.test(imageUrl.trim())) return setError('Use um endereço público HTTPS para a imagem.');
          const referencePrice = price.trim() ? parseAmount(price) : null;
          if (price.trim() && referencePrice === null) return setError('Valor de referência inválido.');
          save.mutate(
            { id: artwork?.id, input: { title: title.trim(), imageUrl: imageUrl.trim(), alternativeText: alt.trim(), style: style.trim(), referencePrice } },
            { onSuccess: onClose },
          );
        }}
      >
        <label className="field">
          <span>Título</span>
          <input value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="field">
          <span>Endereço HTTPS da imagem</span>
          <input value={imageUrl} type="url" onChange={(e) => setImageUrl(e.target.value)} placeholder="https://…" />
          <small className="field-hint">Ainda não há upload: informe uma referência pública.</small>
        </label>
        <label className="field">
          <span>Texto alternativo</span>
          <input value={alt} onChange={(e) => setAlt(e.target.value)} />
        </label>
        <div className="form-grid">
          <label className="field">
            <span>Estilo</span>
            <input value={style} onChange={(e) => setStyle(e.target.value)} />
          </label>
          <label className="field">
            <span>Valor de referência (opcional)</span>
            <input value={price} inputMode="decimal" onChange={(e) => setPrice(e.target.value)} />
          </label>
        </div>
        {(error || save.isError) && (
          <p className="notice error" role="alert">
            {error ?? (save.error instanceof ApiError && Object.values(save.error.fieldErrors)[0]?.[0]) ?? describeError(save.error).title}
          </p>
        )}
        <button type="submit" className="button red full" disabled={save.isPending}>
          {save.isPending ? 'Salvando…' : 'Salvar'}
        </button>
      </form>
    </Dialog>
  );
}

function ArtworkManager({ artistId, kind }: { artistId: string; kind: ArtworkKind }) {
  const works = useArtworks(artistId, kind);
  const remove = useDeleteArtwork(kind);
  const [editing, setEditing] = useState<Artwork | null | 'new'>(null);
  const items = works.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <section>
      <SectionHeading title={kindLabels[kind].title} count={works.isSuccess ? items.length : undefined}>
        <button type="button" className="button outline" onClick={() => setEditing('new')}>
          <Plus size={16} aria-hidden="true" /> Adicionar
        </button>
      </SectionHeading>
      {works.isPending ? (
        <Loading />
      ) : works.isError ? (
        <ErrorState error={works.error} onRetry={() => void works.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title={`Nenhum item em ${kindLabels[kind].title.toLowerCase()}.`} />
      ) : (
        <div className="work-grid">
          {items.map((work) => (
            <figure key={work.id}>
              <img src={work.imageUrl} alt={work.alternativeText} loading="lazy" />
              <figcaption>
                <strong>{work.title}</strong>
                <span>
                  {work.style}
                  {work.referencePrice !== null && ` · ${money(work.referencePrice)}`}
                </span>
                <div className="row">
                  <button type="button" className="icon-button" aria-label={`Editar ${work.title}`} onClick={() => setEditing(work)}>
                    <Pencil size={16} aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    className="icon-button"
                    aria-label={`Remover ${work.title}`}
                    disabled={remove.isPending}
                    onClick={() => {
                      if (window.confirm(`Remover “${work.title}” do portfólio?`)) remove.mutate(work.id);
                    }}
                  >
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                </div>
              </figcaption>
            </figure>
          ))}
        </div>
      )}
      {remove.isError && <p className="notice error">{describeError(remove.error).title}</p>}
      <ArtworkDialog
        key={editing === null ? 'closed' : editing === 'new' ? 'new' : editing.id}
        kind={kind}
        artwork={editing === 'new' || editing === null ? null : editing}
        open={editing !== null}
        onClose={() => setEditing(null)}
      />
    </section>
  );
}

function Content({ session }: { session: Session }) {
  const engagements = useEngagements('artist');
  const fromEngagements = engagements.data?.find((e) => e.artistAccountId === session.profile.id)?.catalog.artistId ?? null;
  const [published, setPublished] = useState<string | null>(() => readStoredId(session.profile.id));
  const artistId = published ?? fromEngagements;
  const artist = useArtist(artistId ?? '', artistId !== null);
  const ready = artistId !== null;

  function onPublished(profile: ArtistProfile) {
    storeId(session.profile.id, profile.id);
    setPublished(profile.id);
  }

  return (
    <>
      <PageHeading eyebrow="SUA ASSINATURA" title="Seu portfólio público.">
        Perfil, tatuagens realizadas e flashes exibidos no catálogo.
      </PageHeading>
      {ready && (
        <Link className="button outline" to={`/artistas/${artistId}`} style={{ marginBottom: 16 }}>
          Ver perfil público <ArrowUpRight size={16} aria-hidden="true" />
        </Link>
      )}
      {ready && artist.isPending ? (
        <Loading />
      ) : ready && artist.isError && !(artist.error instanceof ApiError && artist.error.status === 404) ? (
        <ErrorState error={artist.error} onRetry={() => void artist.refetch()} />
      ) : (
        <ProfileForm key={artist.data?.id ?? 'new'} initial={artist.data ?? null} onPublished={onPublished} />
      )}
      {!ready && (
        <p className="notice" role="status">
          Salve o perfil para carregar e gerenciar tatuagens e flashes neste navegador. Se o perfil já existe, salvá-lo novamente apenas atualiza os dados.
        </p>
      )}
      {ready && (
        <>
          <ArtworkManager artistId={artistId} kind="tattoos" />
          <ArtworkManager artistId={artistId} kind="flashes" />
        </>
      )}
    </>
  );
}

export function PortfolioPage() {
  return <RequireArtist>{(session) => <Content session={session} />}</RequireArtist>;
}
