import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { timeout } from 'rxjs';
import { LibraryApiService } from './core/api/library-api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [DatePipe, NgIcon, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.shell.html',
})
export class App {
  private readonly api = inject(LibraryApiService);
  protected readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly sidebarOpen = signal(false);
  protected readonly ready = signal<'checking' | 'online' | 'offline'>('checking');
  protected readonly now = new Date();

  constructor() {
    const subscription = this.api.getReadyStatus().pipe(timeout(4000)).subscribe({
      next: () => this.ready.set('online'),
      error: () => this.ready.set('offline'),
    });
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  protected closeNav(): void {
    this.sidebarOpen.set(false);
  }

  protected onSearch(event: Event): void {
    const query = (event.target as HTMLInputElement).value.trim();
    if (query.length > 2) {
      this.router.navigate(['/books'], { queryParams: { q: query } });
    }
  }

  protected get sectionName(): string {
    const path = this.router.url.split('?')[0].split('/')[1];
    const sections: Record<string, string> = {
      loans: 'Empréstimos',
      books: 'Catálogo',
      readers: 'Leitores',
      audit: 'Auditoria',
    };
    return sections[path] ?? 'Visão geral';
  }
}
