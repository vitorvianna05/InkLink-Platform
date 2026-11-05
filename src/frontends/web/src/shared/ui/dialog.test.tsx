import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Dialog } from './Dialog';

/** Diálogo desmontado ao fechar, como o Portfólio e a foto de perfil (RNF03). */
function Unmounting({ withMenu = false }: { withMenu?: boolean }) {
  const [menu, setMenu] = useState(withMenu);
  const [open, setOpen] = useState(false);
  const fallback = useRef<HTMLButtonElement>(null);
  return (
    <>
      <button ref={fallback} type="button">
        Conta
      </button>
      {(!withMenu || menu) && (
        <button
          type="button"
          onClick={() => {
            setMenu(false);
            setOpen(true);
          }}
        >
          Abrir
        </button>
      )}
      {open && (
        <Dialog open title="Exemplo" onClose={() => setOpen(false)} returnFocus={withMenu ? () => fallback.current : undefined}>
          <input aria-label="Campo" />
        </Dialog>
      )}
    </>
  );
}

describe('diálogo', () => {
  it('devolve o foco ao controle de origem quando é desmontado ainda aberto', async () => {
    render(<Unmounting />);
    const origin = screen.getByRole('button', { name: 'Abrir' });
    await userEvent.click(origin);
    expect(screen.getByLabelText('Campo')).toHaveFocus();
    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    expect(screen.queryByLabelText('Campo')).not.toBeInTheDocument();
    expect(origin).toHaveFocus();
  });

  it('usa o destino alternativo quando o controle de origem deixou de existir', async () => {
    render(<Unmounting withMenu />);
    await userEvent.click(screen.getByRole('button', { name: 'Abrir' }));
    expect(screen.queryByRole('button', { name: 'Abrir' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    expect(screen.getByRole('button', { name: 'Conta' })).toHaveFocus();
  });
});
