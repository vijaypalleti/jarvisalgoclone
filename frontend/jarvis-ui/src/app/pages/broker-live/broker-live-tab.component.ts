import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OperatorFunction, forkJoin, interval, of, catchError } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import {
  FyersFund, FyersHolding, FyersOrder, FyersPositions, FyersQuote, FyersService, FyersStatus, FyersTrade
} from '../../core/fyers.service';
import { ConsoleStateService } from '../../console/console-state.service';

type Section = 'positions' | 'orders' | 'trades' | 'holdings';

@Component({
  selector: 'app-broker-live-tab',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './broker-live-tab.component.html',
  styleUrl: './broker-live-tab.component.scss',
})
export class BrokerLiveTabComponent {
  private fyers = inject(FyersService);
  private state = inject(ConsoleStateService);

  status = signal<FyersStatus | null>(null);
  funds = signal<FyersFund[]>([]);
  positions = signal<FyersPositions | null>(null);
  orders = signal<FyersOrder[]>([]);
  trades = signal<FyersTrade[]>([]);
  holdings = signal<FyersHolding[]>([]);
  quotes = signal<FyersQuote[]>([]);
  errors = signal<string[]>([]);
  loading = signal(false);
  lastUpdated = signal<Date | null>(null);
  autoRefresh = signal(true);
  section = signal<Section>('positions');

  showToken = signal(false);
  tokenForm = { clientId: '', accessToken: '' };

  fundCards = computed(() => {
    const pick = (id: number) => this.funds().find(f => f.id === id);
    return [1, 2, 10, 4].map(pick).filter((f): f is FyersFund => !!f);
  });
  orderCounts = computed(() => {
    const c: Record<string, number> = {};
    for (const o of this.orders()) c[o.status] = (c[o.status] ?? 0) + 1;
    return c;
  });

  constructor() {
    effect(() => { this.state.refreshTick(); this.load(); });
    interval(5_000).pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      if (this.autoRefresh() && this.status()?.connected) this.load(true);
    });
  }

  load(silent = false) {
    if (!silent) this.loading.set(true);
    const errs: string[] = [];
    const safe = <T>(name: string, fallback: T): OperatorFunction<T, T> => catchError((e: any) => {
      errs.push(`${name}: ${e?.error?.error ?? e?.message ?? 'failed'}`);
      return of(fallback);
    });

    this.fyers.status().pipe(safe('status', null as FyersStatus | null)).subscribe(s => {
      this.status.set(s);
      if (!s?.connected) { this.loading.set(false); this.errors.set(errs); return; }
      forkJoin({
        funds: this.fyers.funds().pipe(safe('funds', [] as FyersFund[])),
        positions: this.fyers.positions().pipe(safe('positions', null as FyersPositions | null)),
        orders: this.fyers.orders().pipe(safe('orders', [] as FyersOrder[])),
        trades: this.fyers.trades().pipe(safe('trades', [] as FyersTrade[])),
        holdings: this.fyers.holdings().pipe(safe('holdings', [] as FyersHolding[])),
        quotes: this.fyers.quotes().pipe(safe('quotes', [] as FyersQuote[])),
      }).subscribe(r => {
        this.funds.set(r.funds); this.positions.set(r.positions); this.orders.set(r.orders);
        this.trades.set(r.trades); this.holdings.set(r.holdings); this.quotes.set(r.quotes);
        this.errors.set(errs); this.lastUpdated.set(new Date()); this.loading.set(false);
      });
    });
  }

  saveToken() {
    const { clientId, accessToken } = this.tokenForm;
    if (!clientId.trim() || !accessToken.trim()) return;
    this.fyers.setToken(clientId, accessToken).subscribe(() => {
      this.tokenForm.accessToken = '';
      this.showToken.set(false);
      this.load();
      this.state.refresh();
    });
  }

  statusTone(s: string) {
    return s === 'COMPLETED' ? 'green' : s === 'REJECTED' ? 'red' : s === 'PENDING' || s === 'TRANSIT' ? 'amber' : 'gray';
  }
  pnlClass(v: number | null | undefined) { return !v ? '' : v > 0 ? 'pos' : 'neg'; }
}
