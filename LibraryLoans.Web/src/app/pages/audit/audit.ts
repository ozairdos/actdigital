import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NgIcon } from '@ng-icons/core';
import { ApiProblem, AuditEvent } from '../../core/api/models';
import { apiErrorMessage, LibraryApiService } from '../../core/api/library-api.service';

@Component({
  selector: 'app-audit-page',
  standalone: true,
  imports: [DatePipe, FormsModule, NgIcon],
  templateUrl: './audit.html',
  styleUrl: './audit.scss',
})
export class AuditPage {
  private readonly api = inject(LibraryApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly events = signal<AuditEvent[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected filter = { entityName: '', entityId: '' };
  protected readonly selected = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getAuditEvents({
      entityName: this.filter.entityName || undefined,
      entityId: this.filter.entityId.trim() || undefined,
      limit: 200,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (events) => { this.events.set(events); this.loading.set(false); },
      error: (error: ApiProblem) => { this.error.set(apiErrorMessage(error)); this.loading.set(false); },
    });
  }

  protected toggleDetails(id: string): void {
    this.selected.set(this.selected() === id ? null : id);
  }
}