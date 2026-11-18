import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApiProblem,
  AuditEvent,
  Availability,
  Book,
  CreateBookRequest,
  CreateLoanRequest,
  CreateReaderRequest,
  Loan,
  Reader,
  UpdateBookRequest,
} from './models';

@Injectable({ providedIn: 'root' })
export class LibraryApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getBooks(): Observable<Book[]> {
    return this.http.get<Book[]>(`${this.baseUrl}/books`);
  }

  getBook(id: string): Observable<Book> {
    return this.http.get<Book>(`${this.baseUrl}/books/${id}`);
  }

  createBook(request: CreateBookRequest): Observable<Book> {
    return this.http.post<Book>(`${this.baseUrl}/books`, request);
  }

  updateBook(id: string, request: UpdateBookRequest): Observable<Book> {
    return this.http.patch<Book>(`${this.baseUrl}/books/${id}`, request);
  }

  deactivateBook(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/books/${id}`);
  }

  getAvailability(id: string): Observable<Availability> {
    return this.http.get<Availability>(`${this.baseUrl}/books/${id}/availability`);
  }

  getBookHistory(id: string): Observable<Loan[]> {
    return this.http.get<Loan[]>(`${this.baseUrl}/books/${id}/history`);
  }

  getReaders(): Observable<Reader[]> {
    return this.http.get<Reader[]>(`${this.baseUrl}/users`);
  }

  getReader(id: string): Observable<Reader> {
    return this.http.get<Reader>(`${this.baseUrl}/users/${id}`);
  }

  createReader(request: CreateReaderRequest): Observable<Reader> {
    return this.http.post<Reader>(`${this.baseUrl}/users`, request);
  }

  getReaderLoans(id: string): Observable<Loan[]> {
    return this.http.get<Loan[]>(`${this.baseUrl}/users/${id}/loans`);
  }

  getLoans(limit = 100): Observable<Loan[]> {
    return this.http.get<Loan[]>(`${this.baseUrl}/loans`, { params: { limit } });
  }

  createLoan(request: CreateLoanRequest, key: string): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans`, request, {
      headers: { 'Idempotency-Key': key },
    });
  }

  returnLoan(id: string): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans/${id}/return`, {});
  }

  cancelLoan(id: string): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans/${id}/cancel`, {});
  }

  getAuditEvents(options: { entityName?: string; entityId?: string; limit?: number } = {}): Observable<AuditEvent[]> {
    let params = new HttpParams().set('limit', options.limit ?? 100);
    if (options.entityName) params = params.set('entityName', options.entityName);
    if (options.entityId) params = params.set('entityId', options.entityId);
    return this.http.get<AuditEvent[]>(`${this.baseUrl}/audit-events`, { params });
  }

  getLiveStatus(): Observable<string> {
    return this.http.get(`${this.baseUrl}/health/live`, { responseType: 'text' });
  }

  getReadyStatus(): Observable<string> {
    return this.http.get(`${this.baseUrl}/health/ready`, { responseType: 'text' });
  }
}

export function apiErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'error' in error) {
    const body = (error as { error?: ApiProblem | string }).error;
    if (typeof body === 'string' && body.length > 0) return body;
    if (body && typeof body === 'object') return body.detail ?? body.title ?? 'A API recusou a operação.';
  }

  return 'Não foi possível conectar à API. Confirme se ela está em execução.';
}