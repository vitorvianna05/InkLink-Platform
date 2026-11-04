import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Search, SlidersHorizontal, Sparkles, X } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import type { ArtistCard, Page } from '@/shared/api/contracts';
import { getJson } from '@/shared/api/http';
import { PageHeading, SectionHeading } from '@/shared/ui/PageHeading';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { emptyCriteria, useArtistSearch, type SearchCriteria } from './api';
import { ArtistGrid } from './ArtistCardView';
import { SearchCombobox } from './SearchCombobox';

const budgets = [300, 500, 800, 1200, 2000];

/** Critérios submetidos ficam na URL: voltar de um perfil restaura a mesma busca. */
function readSubmitted(params: URLSearchParams): SearchCriteria | null {
  if (params.get('buscar') !== '1') return null;
  return { term: params.get('q') ?? '', style: params.get('estilo') ?? '', city: params.get('cidade') ?? '', maxPrice: params.get('ate') ?? '' };
}

function toParams(criteria: SearchCriteria): Record<string, string> {
  const entries: Record<string, string> = { buscar: '1' };
  if (criteria.term) entries.q = criteria.term;
  if (criteria.style) entries.estilo = criteria.style;
  if (criteria.city) entries.cidade = criteria.city;
  if (criteria.maxPrice) entries.ate = criteria.maxPrice;
  return entries;
}

/** Opções dos filtros de seleção derivadas do próprio catálogo publicado. */
function useFilterOptions() {
  return useQuery({
    queryKey: ['catalog', 'filter-options'],
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const page = await getJson<Page<ArtistCard>>('/api/v1/artists', { query: { limit: 100 }, signal });
      const collator = new Intl.Collator('pt-BR');
      const styles = [...new Set(page.items.flatMap((a) => a.styles))].sort(collator.compare);
      const cities = [...new Set(page.items.map((a) => a.city))].sort(collator.compare);
      return { styles, cities };
    },
  });
}

export function ExplorePage() {
  const [params, setParams] = useSearchParams();
  const submitted = useMemo(() => readSubmitted(params), [params]);
  const [draft, setDraft] = useState<SearchCriteria>(() => submitted ?? emptyCriteria);
  const options = useFilterOptions();
  const results = useArtistSearch(submitted);

  const dirty = submitted !== null && (Object.keys(draft) as (keyof SearchCriteria)[]).some((key) => draft[key].trim() !== submitted[key]);
  const artists = results.data?.pages.flatMap((page) => page.items) ?? [];
  const total = results.data?.pages[0]?.total ?? 0;

  function update(key: keyof SearchCriteria, value: string) {
    setDraft((current) => ({ ...current, [key]: value }));
  }

  function clear() {
    setDraft(emptyCriteria);
    setParams({});
  }

  return (
    <section>
      <PageHeading eyebrow="SUA IDEIA É O PONTO DE PARTIDA" title="O próximo traço começa com a sua busca.">
        Encontre quem combina com sua ideia. Escolha o estilo, a cidade e o orçamento para descobrir sua próxima conexão.
      </PageHeading>

      <form
        className="search-form"
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          const next = { ...draft, term: draft.term.trim() };
          setDraft(next);
          setParams(toParams(next));
        }}
      >
        <SearchCombobox value={draft.term} onChange={(value) => update('term', value)} />
        <div className="row muted">
          <SlidersHorizontal size={15} aria-hidden="true" />
          <span>Todos os filtros são opcionais e combinados entre si.</span>
        </div>
        <div className="form-grid">
          <label className="field">
            <span>Estilo de tatuagem</span>
            <select value={draft.style} onChange={(e) => update('style', e.target.value)}>
              <option value="">Todos os estilos</option>
              {options.data?.styles.map((style) => (
                <option key={style}>{style}</option>
              ))}
              {draft.style && !options.data?.styles.includes(draft.style) && <option>{draft.style}</option>}
            </select>
          </label>
          <label className="field">
            <span>Cidade</span>
            <select value={draft.city} onChange={(e) => update('city', e.target.value)}>
              <option value="">Todas as cidades</option>
              {options.data?.cities.map((city) => (
                <option key={city}>{city}</option>
              ))}
              {draft.city && !options.data?.cities.includes(draft.city) && <option>{draft.city}</option>}
            </select>
          </label>
          <label className="field">
            <span>Orçamento por sessão</span>
            <select value={draft.maxPrice} onChange={(e) => update('maxPrice', e.target.value)}>
              <option value="">Qualquer valor</option>
              {budgets.map((value) => (
                <option key={value} value={String(value)}>
                  Até {value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 })}
                </option>
              ))}
            </select>
            <small className="field-hint">Compara com o preço inicial de referência, não com o orçamento final.</small>
          </label>
        </div>
        {options.isError && <p className="notice warning">Não foi possível carregar as opções de estilo e cidade. A busca por texto continua disponível.</p>}
        <div className="form-actions">
          <button type="button" className="text-button" onClick={clear}>
            <X size={14} aria-hidden="true" /> Limpar busca
          </button>
          <button type="submit" className="button red">
            <Search size={17} aria-hidden="true" /> Buscar artistas <ArrowRight size={17} aria-hidden="true" />
          </button>
        </div>
      </form>

      {submitted === null ? (
        <EmptyState title="Cada história tem um traço." icon={<Sparkles size={26} aria-hidden="true" />}>
          <p>Comece pela pesquisa ou escolha os filtros acima. Os artistas aparecem depois que você buscar.</p>
        </EmptyState>
      ) : (
        <section aria-labelledby="results-title">
          <SectionHeading eyebrow="SUA BUSCA, SUAS POSSIBILIDADES" title="Artistas para você" count={results.isSuccess ? total : undefined} />
          <h2 id="results-title" className="visually-hidden">
            Resultados da busca
          </h2>
          <p role="status" className={dirty ? 'notice warning' : 'muted'}>
            {dirty
              ? 'Você alterou os critérios. Clique em Buscar artistas para atualizar os resultados.'
              : results.isSuccess
                ? `${total} ${total === 1 ? 'artista encontrado' : 'artistas encontrados'} para sua pesquisa.`
                : ''}
          </p>
          {results.isPending ? (
            <Loading label="Buscando artistas…" />
          ) : results.isError ? (
            <ErrorState error={results.error} onRetry={() => void results.refetch()} />
          ) : artists.length === 0 ? (
            <EmptyState title="Ainda não encontramos essa conexão.">
              <p>Experimente outro termo, estilo, cidade ou faixa de orçamento.</p>
              <button type="button" className="text-button red" onClick={clear}>
                Limpar busca e tentar novamente <ArrowRight size={15} aria-hidden="true" />
              </button>
            </EmptyState>
          ) : (
            <>
              <ArtistGrid artists={artists} />
              {results.hasNextPage && (
                <div className="row" style={{ justifyContent: 'center', marginTop: 20 }}>
                  <button type="button" className="button outline" onClick={() => void results.fetchNextPage()} disabled={results.isFetchingNextPage}>
                    {results.isFetchingNextPage ? 'Carregando…' : 'Carregar mais artistas'}
                  </button>
                </div>
              )}
            </>
          )}
        </section>
      )}
    </section>
  );
}
