import { LogOut, Menu, X } from 'lucide-react';
import { useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import { SessionNotices } from '@/features/session/guards';
import { useSession } from '@/features/session/session';
import { initials } from '@/shared/lib/format';

const clientLinks = [
  { to: '/', label: 'Descobrir', end: true },
  { to: '/explorar', label: 'Explorar' },
  { to: '/mensagens', label: 'Mensagens' },
  { to: '/meu-espaco', label: 'Meu espaço' },
];

const studioLinks = [
  { to: '/estudio', label: 'Painel', end: true },
  { to: '/estudio/agenda', label: 'Agenda' },
  { to: '/estudio/mensagens', label: 'Mensagens' },
  { to: '/estudio/financeiro', label: 'Financeiro' },
  { to: '/estudio/portfolio', label: 'Portfólio' },
];

export function Layout() {
  const { pathname } = useLocation();
  const { session, status, signIn, signOut, signingIn } = useSession();
  const [menuOpen, setMenuOpen] = useState(false);
  const inStudio = pathname.startsWith('/estudio');
  const links = inStudio ? studioLinks : clientLinks;

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
          <nav className={menuOpen ? 'main-nav open' : 'main-nav'} aria-label={inStudio ? 'Navegação do estúdio' : 'Navegação principal'} id="main-nav" onClick={() => setMenuOpen(false)}>
            {links.map((link) => (
              <NavLink key={link.to} to={link.to} end={link.end} className={({ isActive }) => (isActive ? 'active' : undefined)}>
                {link.label}
              </NavLink>
            ))}
            {session?.isArtist && (
              <NavLink to={inStudio ? '/meu-espaco' : '/estudio'}>{inStudio ? 'Área do cliente' : 'Estúdio do artista'}</NavLink>
            )}
          </nav>
          <div className="nav-right">
            {status === 'authenticated' && session ? (
              <>
                <span className="user-chip">
                  <span className="avatar" aria-hidden="true">
                    {initials(session.profile.displayName)}
                  </span>
                  <span className="user-name">{session.profile.displayName}</span>
                </span>
                <button type="button" className="icon-button" onClick={() => void signOut()} aria-label="Sair da conta">
                  <LogOut size={18} aria-hidden="true" />
                </button>
              </>
            ) : status === 'anonymous' ? (
              <button type="button" className="button white" onClick={signIn} disabled={signingIn}>
                {signingIn ? 'Aguardando login…' : 'Entrar'}
              </button>
            ) : null}
            <button
              type="button"
              className="icon-button menu-button"
              aria-expanded={menuOpen}
              aria-controls="main-nav"
              aria-label={menuOpen ? 'Fechar menu' : 'Abrir menu'}
              onClick={() => setMenuOpen((open) => !open)}
            >
              {menuOpen ? <X size={20} aria-hidden="true" /> : <Menu size={20} aria-hidden="true" />}
            </button>
          </div>
        </div>
      </header>
      <main id="conteudo" tabIndex={-1}>
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
