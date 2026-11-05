import { X } from 'lucide-react';
import { useEffect, useId, useRef, type ReactNode } from 'react';

interface DialogProps {
  open: boolean;
  title: string;
  eyebrow?: string;
  onClose: () => void;
  children: ReactNode;
  wide?: boolean;
  /** Destino do foco quando o controle de origem não existe mais (por exemplo, um item de menu já fechado). */
  returnFocus?: () => HTMLElement | null;
}

/**
 * Diálogo modal nativo: recebe o foco ao abrir, mantém o foco no próprio contexto
 * (showModal torna o restante da página inerte) e devolve o foco ao controle de origem.
 */
export function Dialog({ open, title, eyebrow, onClose, children, wide, returnFocus }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const origin = useRef<Element | null>(null);
  const titleId = useId();
  const restoreFocus = useRef<() => void>(() => undefined);
  // Ref atualizada a cada render: o fechamento usa sempre o returnFocus mais recente.
  useEffect(() => {
    restoreFocus.current = () => {
      const previous = origin.current;
      origin.current = null;
      // body indica que o controle de origem já tinha sumido ao abrir (o menu fechou junto): usa o destino alternativo.
      const usable = previous instanceof HTMLElement && previous.isConnected && previous !== document.body;
      const target = usable ? previous : (returnFocus?.() ?? null);
      target?.focus();
    };
  });

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      origin.current = document.activeElement;
      dialog.showModal();
      const first = dialog.querySelector<HTMLElement>('[data-autofocus], input, textarea, select, button:not(.dialog-close)');
      (first ?? dialog).focus();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    // O evento close é assíncrono: se o diálogo já foi reaberto (remontagem do StrictMode, por exemplo), o
    // evento é antigo e não pode consumir a origem do foco da abertura atual.
    const handleClose = () => {
      if (!dialog.open) restoreFocus.current();
    };
    dialog.addEventListener('close', handleClose);
    return () => {
      dialog.removeEventListener('close', handleClose);
      // Desmontado ainda aberto (o chamador deixou de renderizá-lo): fecha o modal, o que tira a página do
      // estado inerte, e só então devolve o foco ao controle de origem (RNF03).
      if (dialog.open) {
        dialog.close();
        restoreFocus.current();
      }
    };
  }, []);

  return (
    <dialog
      ref={ref}
      className={wide ? 'dialog dialog-wide' : 'dialog'}
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === ref.current) onClose();
      }}
    >
      {open && (
        <div className="dialog-body">
          <button type="button" className="icon-button dialog-close" onClick={onClose} aria-label="Fechar">
            <X size={20} aria-hidden="true" />
          </button>
          {eyebrow && <span className="eyebrow">{eyebrow}</span>}
          <h2 id={titleId}>{title}</h2>
          {children}
        </div>
      )}
    </dialog>
  );
}
