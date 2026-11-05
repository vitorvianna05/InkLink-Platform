import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { loginPath } from '@/features/session/destination';
import { SessionNotices } from '@/features/session/guards';
import { useSession } from '@/features/session/session';
import { StaleDataNotice } from '@/shared/ui/StaleDataNotice';
import { UserMenu, type NavItem } from './UserMenu';

const clientLinks: NavItem[] = [
  { to: '/', label: 'Descobrir', end: true },
  { to: '/explorar', label: 'Explorar' },
  { to: '/mensagens', label: 'Mensagens' },
  { to: '/meu-espaco', label: 'Meu espaço' },
];

const studioLinks: NavItem[] = [
  { to: '/estudio', label: 'Painel', end: true },
  { to: '/estudio/agenda', label: 'Agenda' },
  { to: '/estudio/mensagens', label: 'Mensagens' },
  { to: '/estudio/financeiro', label: 'Financeiro' },
  { to: '/estudio/portfolio', label: 'Portfólio' },
];

export function Layout() {
  const location = useLocation();
  const { session, status } = useSession();
  const inStudio = location.pathname.startsWith('/estudio');
  const links = inStudio ? studioLinks : clientLinks;
  // Artistas alternam entre as áreas pela navegação; o menu da conta oferece o mesmo atalho.
  const areaSwitch: NavItem[] = session?.isArtist ? [inStudio ? { to: '/meu-espaco', label: 'Área do cliente' } : { to: '/estudio', label: 'Estúdio do artista' }] : [];
  const areaLinks = [...links, ...areaSwitch];

  return (
    <>
      <a className="skip-link" href="#conteudo">
        Pular para o conteúdo
      </a>
      <header className="site-header">
        <div className="nav-inner">
          <NavLink className="logo" to="/" aria-label="InkLink, página inicial">
            INK<span>LINK</span>
            <i aria-hidden="true" />
          </NavLink>
          <span className="area-badge" aria-label={`Área atual: ${inStudio ? 'Estúdio do artista' : 'Área do cliente'}`}>
            {inStudio ? 'Estúdio' : 'Cliente'}
          </span>
          <nav className="main-nav" aria-label={inStudio ? 'Navegação do estúdio' : 'Navegação principal'}>
            {areaLinks.map((link) => (
              <NavLink key={link.to} to={link.to} end={link.end} className={({ isActive }) => (isActive ? 'active' : undefined)}>
                {link.label}
              </NavLink>
            ))}
          </nav>
          <div className="nav-right">
            {status === 'anonymous' && (
              <Link className="button white header-signin" to={loginPath(location)}>
                Entrar
              </Link>
            )}
            <UserMenu links={links} inStudio={inStudio} />
          </div>
        </div>
      </header>
      <main id="conteudo" tabIndex={-1}>
        <StaleDataNotice />
        <Outlet />
      </main>
      <footer className="site-footer">
        <span className="logo">
          INK<span>LINK</span>
          <i aria-hidden="true" />
        </span>
        <span>Conectando histórias. Marcando a pele.</span>
        <small>Nenhuma cobrança é feita pela plataforma.</small>
      </footer>
      <SessionNotices />
    </>
  );
}
