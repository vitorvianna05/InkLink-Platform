import { ArrowRight, ArrowUpRight, Sparkles } from 'lucide-react';
import { Link } from 'react-router';
import { useSession } from '@/features/session/session';
import { SectionHeading } from '@/shared/ui/PageHeading';
import { EmptyState, ErrorState, Loading } from '@/shared/ui/States';
import { useFeaturedArtists } from './api';
import { ArtistGrid } from './ArtistCardView';

export function DiscoverPage() {
  const featured = useFeaturedArtists();
  const { session } = useSession();

  return (
    <>
      <section className="hero" aria-labelledby="hero-title">
        <div className="stack">
          <span className="eyebrow light">CONEXÕES QUE MARCAM</span>
          <h1 id="hero-title">
            Sua próxima tattoo começa com uma <em>conexão.</em>
          </h1>
          <p>Encontre seu artista. Compartilhe sua ideia. Leve sua história na pele.</p>
          <div className="row">
            <Link className="button white" to="/explorar">
              Encontre seu artista <ArrowUpRight size={18} aria-hidden="true" />
            </Link>
          </div>
        </div>
        <div className="hero-aside">
          <div className="hero-note">
            <Sparkles size={20} aria-hidden="true" />
            <p>
              Uma ideia na cabeça? <strong>O próximo passo é aqui.</strong> Conheça portfólios, flashes e converse com quem vai traduzir sua história em arte.
            </p>
          </div>
        </div>
      </section>

      <div className="home-layout">
        <section aria-labelledby="featured-title">
          <SectionHeading eyebrow="ENCONTRE SUA CONEXÃO" title="Descubra. Inspire-se. Conecte-se.">
            <Link className="text-button" to="/explorar">
              Ver todos <ArrowRight size={16} aria-hidden="true" />
            </Link>
          </SectionHeading>
          <h2 id="featured-title" className="visually-hidden">
            Artistas em destaque
          </h2>
          {featured.isPending ? (
            <Loading label="Carregando artistas…" />
          ) : featured.isError ? (
            <ErrorState error={featured.error} onRetry={() => void featured.refetch()} />
          ) : featured.data.items.length === 0 ? (
            <EmptyState title="Ainda não há artistas publicados no catálogo." />
          ) : (
            <ArtistGrid artists={featured.data.items} />
          )}
        </section>
        <aside>
          <div className="aside-box">
            <h3>A arte é pessoal. A escolha também.</h3>
            <p className="muted">Pesquise por estilo, cidade e orçamento para encontrar quem combina com a sua ideia.</p>
            <Link className="button red" to="/explorar">
              Explorar artistas <ArrowUpRight size={16} aria-hidden="true" />
            </Link>
          </div>
          <div className="aside-box dark">
            <span className="eyebrow light">PARA QUEM CRIA</span>
            <h3>Seu traço merece ser descoberto.</h3>
            {session?.isArtist ? (
              <Link className="text-button" to="/estudio" style={{ color: '#fff' }}>
                Abrir meu estúdio <ArrowUpRight size={16} aria-hidden="true" />
              </Link>
            ) : (
              <p>Artistas publicam portfólio e flashes e organizam atendimentos pelo estúdio.</p>
            )}
          </div>
        </aside>
      </div>
    </>
  );
}
