import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NgIcon } from '@ng-icons/core';
import { forkJoin } from 'rxjs';
import { ApiProblem, Book, Loan, LoanStatus, Reader } from '../../core/api/models';
import { apiErrorMessage, LibraryApiService } from '../../core/api/library-api.service';
import { loanStatusClass, loanStatusLabel, relativeDueLabel } from '../../shared/loan-presentation';

type StatusFilter = 'All' | LoanStatus;

@Component({
  selector: 'app-loans-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NgIcon],
  templateUrl: './loans.html',
  styleUrl: './loans.scss',
})
export class LoansPage {
  private readonly api = inject(LibraryApiService);
  private readonly destroyRef = inject(DestroyRef);
  private pendingSubmission: { payload: string; key: string } | null = null;
  protected readonly loans = signal<Loan[]>([]);
  protected readonly books = signal<Book[]>([]);
  protected readonly readers = signal<Reader[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly search = signal('');
  protected readonly statusFilter = signal<StatusFilter>('All');
  protected readonly showForm = signal(false);
  protected readonly loanStatusClass = loanStatusClass;
  protected readonly loanStatusLabel = loanStatusLabel;
  protected readonly relativeDueLabel = relativeDueLabel;
  protected form = { bookId: '', readerId: '', dueDate: new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10) };
  protected readonly availableBooks = computed(() => this.books().filter((book) => book.isActive && book.availableCopies > 0));
  protected readonly bookById = computed(() => new Map(this.books().map((book) => [book.id, book])));
  protected readonly readerById = computed(() => new Map(this.readers().map((reader) => [reader.id, reader.name])));
  protected readonly filteredLoans = computed(() => {
    const term = this.search().trim().toLocaleLowerCase('pt-BR');
    return [...this.loans()]
      .filter((loan) => this.statusFilter() === 'All' || loan.status === this.statusFilter())
      .filter((loan) => {
        if (!term) return true;
        const book = this.bookById().get(loan.bookId);
        const reader = this.readerById().get(loan.readerId);
        return `${book?.title ?? ''} ${book?.isbn ?? ''} ${reader ?? ''} ${loan.readerId}`.toLocaleLowerCase('pt-BR').includes(term);
      })
      .sort((a, b) => b.borrowedAtUtc.localeCompare(a.borrowedAtUtc));
  });
  protected readonly activeCount = computed(() => this.loans().filter((loan) => loan.status === 'Active').length);
  protected readonly overdueCount = computed(() => this.loans().filter((loan) => loan.status === 'Active' && new Date(loan.dueAtUtc) < new Date()).length);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({ loans: this.api.getLoans(500), books: this.api.getBooks(), readers: this.api.getReaders() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ loans, books, readers }) => {
          this.loans.set(loans);
          this.books.set(books);
          this.readers.set(readers);
          this.loading.set(false);
        },
        error: (error: ApiProblem) => { this.error.set(apiErrorMessage(error)); this.loading.set(false); },
      });
  }

  protected openCreate(): void {
    if (this.saving()) return;
    this.pendingSubmission = null;
    this.form = {
      bookId: this.availableBooks()[0]?.id ?? '',
      readerId: this.readers()[0]?.id ?? '',
      dueDate: new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10),
    };
    this.error.set('');
    this.showForm.set(true);
  }

  protected createLoan(): void {
    if (this.saving() || !this.form.bookId || !this.form.readerId || !this.form.dueDate) return;
    const dueDate = new Date(`${this.form.dueDate}T12:00:00`);
    if (!Number.isFinite(dueDate.getTime()) || dueDate <= new Date()) {
      this.error.set('Selecione uma data de devolução futura.');
      return;
    }
    const request = { bookId: this.form.bookId, readerId: this.form.readerId, dueAtUtc: dueDate.toISOString() };
    const payload = JSON.stringify(request);
    if (this.pendingSubmission?.payload !== payload) {
      this.pendingSubmission = { payload, key: crypto.randomUUID() };
    }
    this.saving.set(true);
    this.error.set('');
    this.api.createLoan(request, this.pendingSubmission.key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.pendingSubmission = null; this.saving.set(false); this.showForm.set(false); this.load(); },
        error: (error: ApiProblem) => { this.saving.set(false); this.error.set(apiErrorMessage(error)); },
      });
  }

  protected closeForm(): void {
    if (!this.saving()) this.showForm.set(false);
  }

  protected returnLoan(loan: Loan): void {
    this.api.returnLoan(loan.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.load(),
      error: (error: ApiProblem) => this.error.set(apiErrorMessage(error)),
    });
  }

  protected cancelLoan(loan: Loan): void {
    if (!window.confirm('Cancelar este empréstimo e devolver o exemplar à disponibilidade?')) return;
    this.api.cancelLoan(loan.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.load(),
      error: (error: ApiProblem) => this.error.set(apiErrorMessage(error)),
    });
  }
}
