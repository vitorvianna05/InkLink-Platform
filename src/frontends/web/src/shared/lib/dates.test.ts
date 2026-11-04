import { describe, expect, it } from 'vitest';
import { addMonths, localToInstant, monthGrid } from './dates';

describe('datas da agenda', () => {
  it('gera instante com offset explícito do fuso do artista', () => {
    expect(localToInstant('2026-11-10', '14:00', 'America/Sao_Paulo')).toBe('2026-11-10T14:00:00-03:00');
    expect(localToInstant('2026-07-01', '09:30', 'Europe/Lisbon')).toBe('2026-07-01T09:30:00+01:00');
  });

  it('rejeita horário inexistente ou ambíguo na mudança de horário', () => {
    expect(localToInstant('2026-03-08', '02:30', 'America/New_York')).toBeNull();
    expect(localToInstant('2026-11-01', '01:30', 'America/New_York')).toBeNull();
  });

  it('monta semanas completas do mês', () => {
    const grid = monthGrid('2026-11-01');
    expect(grid.length % 7).toBe(0);
    expect(grid[0]).toBe('2026-11-01');
    expect(grid).toContain('2026-11-30');
  });

  it('avança meses a partir do primeiro dia', () => {
    expect(addMonths('2026-01-31', 1)).toBe('2026-02-01');
  });
});
