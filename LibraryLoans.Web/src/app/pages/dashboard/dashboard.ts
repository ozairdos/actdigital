import { DatePipe, UpperCasePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { forkJoin } from 'rxjs';
import { ApiProblem, AuditEvent, Book, Loan, Reader } from '../../core/api/models';
import { apiErrorMessage, LibraryApiService } from '../../core/api/library-api.service';
import { loanStatusClass, loanStatusLabel, relativeDueLabel } from '../../shared/loan-presentation';

interface WeeklyActivity {
  label: string;
  count: number;
  height: number;
}

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [DatePipe, NgIcon, RouterLink, UpperCasePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardPage {
  private readonly api = inject(LibraryApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly books = signal<Book[]>([]);
  protected readonly readers = signal<Reader[]>([]);
  protected readonly loans = signal<Loan[]>([]);
  protected readonly audit = signal<AuditEvent[]>([]);
  protected readonly loading = signal(true);
  protected readonly today = new Date();
  protected readonly error = signal('');
  protected readonly loanStatusClass = loanStatusClass;
  protected readonly loanStatusLabel = loanStatusLabel;
  protected readonly relativeDueLabel = relativeDueLabel;
  protected readonly activeLoans = computed(() => this.loans().filter((loan) => loan.status === 'Active'));
  protected readonly overdueLoans = computed(() => this.activeLoans().filter((loan) => new Date(loan.dueAtUtc) < new Date()));
  protected readonly availableCopies = computed(() => this.books().reduce((sum, book) => sum + book.availableCopies, 0));
  protected readonly totalCopies = computed(() => this.books().reduce((sum, book) => sum + book.totalCopies, 0));
  protected readonly inventoryFill = computed(() => this.totalCopies() === 0 ? 0 : Math.round((this.availableCopies() / this.totalCopies()) * 100));
  protected readonly bookById = computed(() => new Map(this.books().map((book) => [book.id, book.title])));
  protected readonly weeklyActivity = computed(() => {
    const today = new Date();
    const days = Array.from({ length: 7 }, (_, index) => {
      const date = new Date(today);
      date.setDate(today.getDate() - (6 - index));
      const count = this.loans().filter((loan) => new Date(loan.borrowedAtUtc).toDateString() === date.toDateString()).length;
      return { label: date.toLocaleDateString('pt-BR', { weekday: 'short' }).replace('.', ''), count, height: 10 };
    });
    const max = Math.max(1, ...days.map((day) => day.count));
    return days.map((day) => ({ ...day, height: day.count === 0 ? 8 : Math.max(22, Math.round((day.count / max) * 100)) }));
  });
  protected readonly recentLoans = computed(() => [...this.loans()].sort((a, b) => b.borrowedAtUtc.localeCompare(a.borrowedAtUtc)).slice(0, 5));

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      books: this.api.getBooks(),
      readers: this.api.getReaders(),
      loans: this.api.getLoans(200),
      audit: this.api.getAuditEvents({ limit: 8 }),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ books, readers, loans, audit }) => {
        this.books.set(books);
        this.readers.set(readers);
        this.loans.set(loans);
        this.audit.set(audit);
        this.loading.set(false);
      },
      error: (error: ApiProblem) => {
        this.error.set(apiErrorMessage(error));
        this.loading.set(false);
      },
    });
  }
}
