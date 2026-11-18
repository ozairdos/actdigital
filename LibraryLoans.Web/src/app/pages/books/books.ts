import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { Book, Loan } from '../../core/api/models';
import { apiErrorMessage, LibraryApiService } from '../../core/api/library-api.service';
import { loanStatusClass, loanStatusLabel } from '../../shared/loan-presentation';

@Component({
  selector: 'app-books-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NgIcon],
  templateUrl: './books.html',
  styleUrl: './books.scss',
})
export class BooksPage {
  private readonly api = inject(LibraryApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly books = signal<Book[]>([]);
  protected readonly history = signal<Loan[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly query = signal('');
  protected readonly selectedBook = signal<Book | null>(null);
  protected readonly showForm = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly loanStatusClass = loanStatusClass;
  protected readonly loanStatusLabel = loanStatusLabel;
  protected readonly filteredBooks = computed(() => {
    const query = this.query().trim().toLocaleLowerCase('pt-BR');
    if (!query) return this.books();
    return this.books().filter((book) => `${book.title} ${book.author} ${book.isbn}`.toLocaleLowerCase('pt-BR').includes(query));
  });
  protected form = { title: '', isbn: '', author: '', totalCopies: 1 };

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.query.set(params.get('q') ?? '');
    });
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getBooks().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (books) => { this.books.set(books); this.loading.set(false); },
      error: (error) => { this.error.set(apiErrorMessage(error)); this.loading.set(false); },
    });
  }

  protected openCreate(): void {
    this.editingId.set(null);
    this.form = { title: '', isbn: '', author: '', totalCopies: 1 };
    this.error.set('');
    this.showForm.set(true);
  }

  protected openEdit(book: Book): void {
    this.editingId.set(book.id);
    this.form = { title: book.title, isbn: book.isbn, author: book.author, totalCopies: book.totalCopies };
    this.error.set('');
    this.showForm.set(true);
  }

  protected saveBook(): void {
    if (this.saving()) return;
    if (!this.form.title.trim() || !this.form.author.trim() || !this.form.isbn.trim()
      || !Number.isInteger(this.form.totalCopies) || this.form.totalCopies < 0) {
      this.error.set('Preencha título, autor, ISBN e uma quantidade inteira de exemplares.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    const request = { ...this.form, totalCopies: Number(this.form.totalCopies) };
    const editingId = this.editingId();
    const action = editingId ? this.api.updateBook(editingId, request) : this.api.createBook(request);
    action.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.showForm.set(false); this.load(); },
      error: (error) => { this.saving.set(false); this.error.set(apiErrorMessage(error)); },
    });
  }

  protected selectBook(book: Book): void {
    this.selectedBook.set(book);
    this.history.set([]);
    this.api.getBookHistory(book.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (loans) => { if (this.selectedBook()?.id === book.id) this.history.set(loans); },
      error: (error) => { if (this.selectedBook()?.id === book.id) this.error.set(apiErrorMessage(error)); },
    });
  }

  protected deactivateBook(book: Book): void {
    if (!window.confirm(`Desativar “${book.title}”? O histórico será preservado.`)) return;
    this.api.deactivateBook(book.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.selectedBook.set(null); this.load(); },
      error: (error) => this.error.set(apiErrorMessage(error)),
    });
  }
}
