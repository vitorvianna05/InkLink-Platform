import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ArtistCard, ArtistInput, ArtistProfile, Artwork, ArtworkInput, ArtworkKind, Page, Suggestion } from '@/shared/api/contracts';
import { getJson, request } from '@/shared/api/http';

export interface SearchCriteria {
  term: string;
  style: string;
  city: string;
  maxPrice: string;
}

export const emptyCriteria: SearchCriteria = { term: '', style: '', city: '', maxPrice: '' };

export const catalogKeys = {
  featured: ['catalog', 'featured'] as const,
  search: (criteria: SearchCriteria) => ['catalog', 'search', criteria] as const,
  suggestions: (term: string) => ['catalog', 'suggestions', term] as const,
  artist: (id: string) => ['catalog', 'artist', id] as const,
  artworks: (id: string, kind: ArtworkKind) => ['catalog', 'artist', id, kind] as const,
  favorites: ['catalog', 'favorites'] as const,
};

export function useFeaturedArtists() {
  return useQuery({
    queryKey: catalogKeys.featured,
    queryFn: ({ signal }) => getJson<Page<ArtistCard>>('/api/v1/artists', { query: { limit: 6 }, signal }),
  });
}

/** Busca explícita: só executa com critérios submetidos (null = estado inicial sem cards). */
export function useArtistSearch(criteria: SearchCriteria | null) {
  return useInfiniteQuery({
    queryKey: catalogKeys.search(criteria ?? emptyCriteria),
    enabled: criteria !== null,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) =>
      getJson<Page<ArtistCard>>('/api/v1/artists/search', {
        query: {
          term: criteria?.term,
          style: criteria?.style,
          city: criteria?.city,
          maxPrice: criteria?.maxPrice,
          limit: 12,
          cursor: pageParam,
        },
        signal,
      }),
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : null),
  });
}

/**
 * Sugestões por termo. A chave inclui o termo e o sinal de cancelamento aborta a consulta
 * anterior, garantindo que uma resposta antiga não substitua a de uma digitação mais recente.
 */
export function useSuggestions(term: string, enabled = true) {
  const trimmed = term.trim();
  return useQuery({
    queryKey: catalogKeys.suggestions(trimmed),
    enabled: enabled && trimmed.length > 0,
    staleTime: 30_000,
    queryFn: ({ signal }) => getJson<Suggestion[]>('/api/v1/artists/suggestions', { query: { term: trimmed, limit: 8 }, signal }),
  });
}

export function useArtist(id: string, enabled = true) {
  return useQuery({ queryKey: catalogKeys.artist(id), enabled: enabled && Boolean(id), queryFn: ({ signal }) => getJson<ArtistProfile>(`/api/v1/artists/${id}`, { signal }) });
}

export function useArtworks(id: string, kind: ArtworkKind, enabled = true) {
  return useInfiniteQuery({
    queryKey: catalogKeys.artworks(id, kind),
    enabled,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) =>
      getJson<Page<Artwork>>(`/api/v1/artists/${id}/${kind}`, { query: { limit: 12, cursor: pageParam }, signal }),
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : null),
  });
}

/** Favoritos do cliente autenticado (lista completa, paginada no servidor). */
export function useFavorites(enabled: boolean) {
  return useQuery({
    queryKey: catalogKeys.favorites,
    enabled,
    queryFn: async ({ signal }) => {
      const items: ArtistCard[] = [];
      let cursor: string | null = null;
      for (let i = 0; i < 10; i++) {
        const page: Page<ArtistCard> = await getJson<Page<ArtistCard>>('/api/v1/favorites', { query: { limit: 100, cursor }, signal });
        items.push(...page.items);
        if (!page.hasMore) break;
        cursor = page.nextCursor;
      }
      return items;
    },
  });
}

export function useToggleFavorite() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ artistId, favorite }: { artistId: string; favorite: boolean }) =>
      request(`/api/v1/favorites/${artistId}`, { method: favorite ? 'PUT' : 'DELETE' }),
    onSettled: () => client.invalidateQueries({ queryKey: catalogKeys.favorites }),
  });
}

// Publicação pelo próprio artista (P03): referências HTTPS de imagem e texto alternativo.
export function usePublishArtist() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: ArtistInput) => request<ArtistProfile>('/api/v1/artists/me', { method: 'PUT', body: input }).then((r) => r.data),
    onSuccess: (profile) => {
      client.setQueryData(catalogKeys.artist(profile.id), profile);
      void client.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

export function useSaveArtwork(kind: ArtworkKind) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id?: string; input: ArtworkInput }) => {
      // Decisão 0018: a imagem é enviada como arquivo, por multipart, na API v2.
      const form = new FormData();
      form.set('title', input.title);
      form.set('alternativeText', input.alternativeText);
      form.set('style', input.style);
      if (input.referencePrice !== null) form.set('referencePrice', input.referencePrice.toFixed(2));
      if (input.image) form.set('image', input.image, input.image.name);
      return id
        ? request(`/api/v2/artists/me/${kind}/${id}`, { method: 'PUT', body: form })
        : request(`/api/v2/artists/me/${kind}`, { method: 'POST', body: form });
    },
    onSettled: () => client.invalidateQueries({ queryKey: ['catalog', 'artist'] }),
  });
}

export function useDeleteArtwork(kind: ArtworkKind) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => request(`/api/v2/artists/me/${kind}/${id}`, { method: 'DELETE' }),
    onSettled: () => client.invalidateQueries({ queryKey: ['catalog', 'artist'] }),
  });
}
