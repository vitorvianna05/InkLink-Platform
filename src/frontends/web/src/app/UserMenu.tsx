import { Camera, LogIn, LogOut, Menu, X } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { Link, NavLink, useLocation } from 'react-router';
import { loginPath } from '@/features/session/destination';
import { ProfilePhotoDialog } from '@/features/session/ProfilePhotoDialog';
import { useSession } from '@/features/session/session';
import { Avatar } from '@/shared/ui/Avatar';

export interface NavItem {
  to: string;
  label: string;
  end?: boolean;
}

/**
 * Menu da conta (T10.12): o botão de três linhas abre as informações e ações do usuário.
 * Em telas estreitas o mesmo painel também reúne os links da área atual.
 */
export function UserMenu({ links, inStudio }: { links: NavItem[]; inStudio: boolean }) {
  const location = useLocation();
  const { status, session, expired, reauthenticate, signingIn, signOut, retry } = useSession();
  // O menu fica associado ao endereço em que foi aberto: navegar por um link do painel o fecha.
  const route = `${location.pathname}${location.search}`;
  const [openAt, setOpenAt] = useState<string | null>(null);
  const open = openAt === route;
  const setOpen = (value: boolean) => setOpenAt(value ? route : null);
  const wrapper = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const [photoOpen, setPhotoOpen] = useState(false);
  const signInHref = loginPath(location);
  const account = status === 'authenticated' && session && !expired ? session : null;
  // Atalho para a outra área: Meu espaço a partir do estúdio; Estúdio do artista somente com a função Artist.
  const shortcuts: NavItem[] = !account ? [] : inStudio ? [{ to: '/meu-espaco', label: 'Meu espaço' }] : account.isArtist ? [{ to: '/estudio', label: 'Estúdio do artista' }] : [];

  useEffect(() => {
    if (!open) return;
    // O primeiro item visível recebe o foco; a navegação do painel fica oculta em telas largas.
    for (const item of panel.current?.querySelectorAll<HTMLElement>('a[href], button:not([disabled])') ?? []) {
      item.focus();
      if (document.activeElement === item) break;
    }
    const onPointerDown = (event: PointerEvent) => {
      if (wrapper.current && !wrapper.current.contains(event.target as Node)) setOpenAt(null);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  const close = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) button.current?.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && open) {
      event.stopPropagation();
      close(true);
    }
  };

  const handleSignOut = async () => {
    close(true);
    await signOut();
  };

  return (
    <div
      className="user-menu"
      ref={wrapper}
      onKeyDown={onKeyDown}
      onBlur={(event) => {
        if (open && !wrapper.current?.contains(event.relatedTarget as Node | null) && event.relatedTarget) setOpen(false);
      }}
    >
      <button
        ref={button}
        type="button"
        className={account ? 'user-menu-button with-avatar' : 'user-menu-button'}
        aria-expanded={open}
        aria-controls={panelId}
        aria-label={open ? 'Fechar menu da conta' : 'Abrir menu da conta'}
        onClick={() => setOpen(!open)}
      >
        {account && <Avatar className="avatar small" name={account.profile.displayName} photoUrl={account.profile.photoThumbnailUrl} />}
        {open ? <X size={20} aria-hidden="true" /> : <Menu size={20} aria-hidden="true" />}
      </button>
      {open && (
        <div className="user-menu-panel" id={panelId} ref={panel}>
          <div className="user-menu-section user-menu-account">
            {status === 'loading' ? (
              <p className="user-menu-note" role="status">
                Verificando sua sessão…
              </p>
            ) : status === 'error' ? (
              <>
                <p className="user-menu-note">Não foi possível verificar sua sessão.</p>
                <button type="button" className="button outline full" onClick={retry}>
                  Tentar novamente
                </button>
              </>
            ) : expired ? (
              <>
                <p className="user-menu-note">Sua sessão expirou. Entre novamente para continuar de onde parou.</p>
                <button type="button" className="button red full" onClick={reauthenticate} disabled={signingIn}>
                  {signingIn ? 'Aguardando login…' : 'Entrar novamente'}
                </button>
              </>
            ) : account ? (
              <div className="user-menu-identity">
                <Avatar name={account.profile.displayName} photoUrl={account.profile.photoThumbnailUrl} />
                <div>
                  <strong>{account.profile.displayName}</strong>
                  <small>{account.isArtist ? 'Artista' : 'Cliente'}</small>
                </div>
              </div>
            ) : (
              <>
                <p className="user-menu-note">Entre para acompanhar pedidos, conversas e favoritos.</p>
                <Link className="button red full" to={signInHref}>
                  <LogIn size={18} aria-hidden="true" />
                  Entrar
                </Link>
              </>
            )}
          </div>

          <div className="user-menu-section user-menu-nav">
            <span className="user-menu-label">{inStudio ? 'Estúdio' : 'Navegação'}</span>
            {links.map((link) => (
              <NavLink key={link.to} to={link.to} end={link.end} className={({ isActive }) => (isActive ? 'active' : undefined)}>
                {link.label}
              </NavLink>
            ))}
          </div>

          {account && (
            <>
              {shortcuts.length > 0 && (
                <div className="user-menu-section user-menu-links">
                  <span className="user-menu-label">Trocar de área</span>
                  {shortcuts.map((link) => (
                    <Link key={link.to} to={link.to}>
                      {link.label}
                    </Link>
                  ))}
                </div>
              )}
              <div className="user-menu-section">
                <button
                  type="button"
                  className="user-menu-signout"
                  onClick={() => {
                    setOpen(false);
                    setPhotoOpen(true);
                  }}
                >
                  <Camera size={18} aria-hidden="true" />
                  Foto de perfil
                </button>
                <button type="button" className="user-menu-signout" onClick={() => void handleSignOut()}>
                  <LogOut size={18} aria-hidden="true" />
                  Sair da conta
                </button>
              </div>
            </>
          )}
        </div>
      )}
      {account && photoOpen && (
        <ProfilePhotoDialog
          session={account}
          open={photoOpen}
          onClose={() => {
            setPhotoOpen(false);
            button.current?.focus();
          }}
        />
      )}
    </div>
  );
}
