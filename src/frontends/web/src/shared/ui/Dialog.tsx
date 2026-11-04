import { X } from 'lucide-react';
import { useEffect, useId, useRef, type ReactNode } from 'react';

interface DialogProps {
  open: boolean;
  title: string;
  eyebrow?: string;
  onClose: () => void;
  children: ReactNode;
  wide?: boolean;
}

/**
 * Diálogo modal nativo: recebe o foco ao abrir, mantém o foco no próprio contexto
 * (showModal torna o restante da página inerte) e devolve o foco ao controle de origem.
 */
export function Dialog({ open, title, eyebrow, onClose, children, wide }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const origin = useRef<Element | null>(null);
  const titleId = useId();

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
    const handleClose = () => {
      const target = origin.current;
      if (target instanceof HTMLElement && target.isConnected) target.focus();
    };
    dialog.addEventListener('close', handleClose);
    return () => dialog.removeEventListener('close', handleClose);
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
