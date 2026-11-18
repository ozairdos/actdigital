import { LoanStatus } from '../core/api/models';

export function loanStatusLabel(status: LoanStatus): string {
  switch (status) {
    case 'Active': return 'Em andamento';
    case 'Returned': return 'Devolvido';
    case 'Canceled': return 'Cancelado';
  }
}

export function loanStatusClass(status: LoanStatus): string {
  switch (status) {
    case 'Active': return 'status-active';
    case 'Returned': return 'status-returned';
    case 'Canceled': return 'status-canceled';
  }
}

export function relativeDueLabel(dueAtUtc: string, now = new Date()): string {
  const days = Math.ceil((new Date(dueAtUtc).getTime() - now.getTime()) / 86_400_000);
  if (days < 0) return `Atrasado ${Math.abs(days)}d`;
  if (days === 0) return 'Vence hoje';
  if (days === 1) return 'Vence amanhã';
  return `Em ${days} dias`;
}