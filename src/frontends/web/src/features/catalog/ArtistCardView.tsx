import { ArrowUpRight, Heart, MapPin } from 'lucide-react';
import { Link, useLocation } from 'react-router';
import { useSession } from '@/features/session/session';
import type { ArtistCard } from '@/shared/api/contracts';
import { money } from '@/shared/lib/format';
import { Avatar } from '@/shared/ui/Avatar';
import { useFavorites, useToggleFavorite } from './api';

/** Botão de favorito persistente: visitantes são convidados a entrar. */
export function FavoriteButton({ artistId, artistName }: { artistId: string; artistName: string }) {
  const { session, signIn, status } = useSession();
  const favorites = useFavorites(session !== null);
  const toggle = useToggleFavorite();
  const active = favorites.data?.some((artist) => artist.id === artistId) ?? false;
  const pending = toggle.isPending && toggle.variables?.artistId === artistId;
  const shown = pending ? toggle.variables!.favorite : active;

  return (
    <button
      type="button"
      className={shown ? 'icon-button favorite active' : 'icon-button favorite'}
      aria-pressed={shown}
      aria-label={shown ? `Remover ${artistName} dos favoritos` : `Salvar ${artistName} nos favoritos`}
      disabled={status === 'loading' || status === 'error' || pending || (session !== null && favorites.isPending)}
      onClick={() => (session ? toggle.mutate({ artistId, favorite: !active }) : signIn())}
    >
      <Heart size={18} fill={shown ? 'currentColor' : 'none'} aria-hidden="true" />
    </button>
  );
}

export function ArtistCardView({ artist }: { artist: ArtistCard }) {
  const location = useLocation();
  return (
    <article className="artist-card">
      <div className="card-title">
        <Avatar className="avatar light" name={artist.name} photoUrl={artist.photoThumbnailUrl} />
        <div>
          <h3>{artist.name}</h3>
          <p className="muted">{artist.studio}</p>
        </div>
        <FavoriteButton artistId={artist.id} artistName={artist.name} />
      </div>
      <p className="location">
        <MapPin size={13} aria-hidden="true" />
        {artist.city}
      </p>
      <div className="pill-row">
        {artist.styles.map((style) => (
          <span key={style} className="pill">
            {style}
          </span>
        ))}
      </div>
      <div className="card-bottom">
        <div>
          <small>A PARTIR DE</small>
          <strong>{money(artist.startingPrice)}</strong>
        </div>
        <Link
          className="button dark"
          to={`/artistas/${artist.id}`}
          state={{ from: `${location.pathname}${location.search}` }}
          aria-label={`Conhecer ${artist.name}`}
        >
          Conhecer <ArrowUpRight size={16} aria-hidden="true" />
        </Link>
      </div>
    </article>
  );
}

export function ArtistGrid({ artists }: { artists: ArtistCard[] }) {
  return (
    <div className="artist-grid">
      {artists.map((artist) => (
        <ArtistCardView key={artist.id} artist={artist} />
      ))}
    </div>
  );
}
