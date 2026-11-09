import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../core/api.service';
import { OverviewSummary } from '../core/models';

/** Shared state for the Strategy Performance console: overview summary + a refresh tick tabs can react to. */
@Injectable({ providedIn: 'root' })
export class ConsoleStateService {
  private api = inject(ApiService);

  readonly overview = signal<OverviewSummary | null>(null);
  readonly alertsOpen = signal(0);
  readonly refreshTick = signal(0);
  readonly loading = signal(false);

  refresh() {
    this.loading.set(true);
    this.api.overview().subscribe({
      next: o => { this.overview.set(o); this.alertsOpen.set(o.logsErrors.errors); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    this.refreshTick.update(t => t + 1);
  }
}
