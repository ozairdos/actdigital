export type LoanStatus = 'Active' | 'Returned' | 'Canceled';

export interface Book {
  id: string;
  title: string;
  isbn: string;
  author: string;
  totalCopies: number;
  availableCopies: number;
  isActive: boolean;
}

export interface Reader {
  id: string;
  name: string;
  createdAtUtc: string;
}

export interface Loan {
  id: string;
  bookId: string;
  readerId: string;
  borrowedAtUtc: string;
  dueAtUtc: string;
  returnedAtUtc: string | null;
  canceledAtUtc: string | null;
  status: LoanStatus;
}

export interface AuditEvent {
  id: string;
  entityName: string;
  entityId: string;
  action: string;
  actor: string;
  timestampUtc: string;
  correlationId: string;
  details: string;
}

export interface CreateBookRequest {
  title: string;
  isbn: string;
  author: string;
  totalCopies: number;
}

export interface UpdateBookRequest {
  title?: string | null;
  isbn?: string | null;
  author?: string | null;
  totalCopies?: number | null;
}

export interface CreateReaderRequest {
  name: string;
}

export interface CreateLoanRequest {
  bookId: string;
  readerId: string;
  dueAtUtc: string;
}

export interface Availability {
  bookId: string;
  availableCopies: number;
}

export interface ApiProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
}