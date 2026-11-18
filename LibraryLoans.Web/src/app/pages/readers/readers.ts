import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NgIcon } from '@ng-icons/core';
import { Reader, Loan } from '../../core/api/models';
import { apiErrorMessage, LibraryApiService } from '../../core/api/library-api.service';
import { loanStatusClass, loanStatusLabel } from '../../shared/loan-presentation';

@Component({
  selector: 'app-readers-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NgIcon],
  templateUrl: './readers.html',
  styleUrl: './readers.scss',
})
export class ReadersPage {
  private readonly api = inject(LibraryApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly readers = signal<Reader[]>([]);
  protected readonly loans = signal<Loan[]>([]);
  protected readonly selectedReader = signal<Reader | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly showForm = signal(false);
  protected readonly error = signal('');
  protected readonly search = signal('');
  protected readonly loanStatusClass = loanStatusClass;
  protected readonly loanStatusLabel = loanStatusLabel;
  protected form = { name: '' };
  protected readonly filteredReaders = computed(() => {
    const query = this.search().trim().toLocaleLowerCase('pt-BR');
    if (!query) return this.readers();
    return this.readers().filter((reader) => reader.name.toLocaleLowerCase('pt-BR').includes(query) || reader.id.includes(query));
  });
  protected readonly activeReaderLoans = computed(() => this.loans().filter((loan) => loan.status === 'Active').length);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getReaders().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (readers) => { this.readers.set(readers); this.loading.set(false); },
      error: (error) => { this.error.set(apiErrorMessage(error)); this.loading.set(false); },
    });
  }

  protected selectReader(reader: Reader): void {
    this.selectedReader.set(reader);
    this.loans.set([]);
    this.api.getReaderLoans(reader.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (loans) => { if (this.selectedReader()?.id === reader.id) this.loans.set(loans); },
      error: (error) => { if (this.selectedReader()?.id === reader.id) this.error.set(apiErrorMessage(error)); },
    });
  }

  protected openCreate(): void {
    this.form = { name: '' };
    this.error.set('');
    this.showForm.set(true);
  }

  protected createReader(): void {
    if (this.saving()) return;
    if (!this.form.name.trim()) {
      this.error.set('Informe o nome do leitor.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api.createReader({ name: this.form.name.trim() }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (reader) => {
        this.saving.set(false);
        this.showForm.set(false);
        this.load();
        this.selectReader(reader);
      },
      error: (error) => { this.saving.set(false); this.error.set(apiErrorMessage(error)); },
    });
  }
}
