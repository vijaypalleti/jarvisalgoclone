import { Component, DestroyRef, HostListener, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OperatorFunction, forkJoin, interval, of, catchError } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { IconComponent } from '../../shared/icon.component';
import {
  FyersAuthConfig, FyersFund, FyersHolding, FyersOrder, FyersPositions, FyersQuote, FyersService, FyersStatus, FyersTrade
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

  // ---- in-app login
  auth = signal<FyersAuthConfig | null>(null);
  loginOpen = signal(false);          // login panel visible
  awaitingPaste = signal(false);      // redirect is NOT our callback (or user chose to paste) -> paste box
  authBusy = signal(false);
  authMsg = signal<{ ok: boolean; text: string } | null>(null);
  pasteInput = '';
  pin = '';
  lastLoginUrl = '';
  private popup: Window | null = null;
  private popupTimer?: ReturnType<typeof setInterval>;

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
    this.loadAuth();
    inject(DestroyRef).onDestroy(() => clearInterval(this.popupTimer));
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

  loadAuth() { this.fyers.authConfig().subscribe({ next: c => this.auth.set(c), error: () => this.auth.set(null) }); }

  /** One click: open the Fyers login in a popup. With our callback as redirect URI it finishes by itself. */
  login() {
    this.authMsg.set(null);
    this.authBusy.set(true);
    this.fyers.authStart().subscribe({
      next: r => {
        this.authBusy.set(false);
        this.loginOpen.set(true);
        this.awaitingPaste.set(!r.oneClick);
        this.lastLoginUrl = r.url;
        this.popup = window.open(r.url, 'fyersLogin', 'width=520,height=760');
        if (!this.popup) this.authMsg.set({ ok: false, text: 'Popup blocked: allow popups for this site, or use the link below.' });
        // fallback if the postMessage is missed: when the popup closes, re-check the status
        clearInterval(this.popupTimer);
        this.popupTimer = setInterval(() => {
          if (this.popup && this.popup.closed) { clearInterval(this.popupTimer); this.afterAuth(); }
        }, 800);
      },
      error: e => { this.authBusy.set(false); this.loginOpen.set(true); this.authMsg.set({ ok: false, text: e?.error?.error ?? 'Could not start login' }); },
    });
  }

  /** The callback page posts {type:'fyers-auth', ok} to us before closing. */
  @HostListener('window:message', ['$event'])
  onMessage(e: MessageEvent) {
    if (e.data?.type !== 'fyers-auth') return;
    clearInterval(this.popupTimer);
    if (e.data.ok) this.afterAuth('Fyers connected');
    else this.authMsg.set({ ok: false, text: 'Login did not complete. Try again, or paste the redirected URL.' });
  }

  submitPaste() {
    if (!this.pasteInput.trim()) return;
    this.authBusy.set(true);
    this.fyers.authExchange(this.pasteInput).subscribe({
      next: r => { this.pasteInput = ''; this.afterAuth(r.message); },
      error: e => { this.authBusy.set(false); this.authMsg.set({ ok: false, text: e?.error?.error ?? 'Exchange failed' }); },
    });
  }

  renew() {
    if (!this.pin.trim()) return;
    this.authBusy.set(true);
    this.fyers.authRenew(this.pin).subscribe({
      next: r => { this.pin = ''; this.afterAuth(r.message); },
      error: e => { this.pin = ''; this.authBusy.set(false); this.authMsg.set({ ok: false, text: e?.error?.error ?? 'Renew failed' }); },
    });
  }

  private afterAuth(msg?: string) {
    this.authBusy.set(false);
    this.loadAuth();
    this.fyers.status().subscribe(s => {
      this.status.set(s);
      if (s.connected) {
        this.authMsg.set({ ok: true, text: msg ?? 'Fyers connected' });
        this.awaitingPaste.set(false);
        setTimeout(() => { this.loginOpen.set(false); this.authMsg.set(null); }, 2500);
        this.load(); this.state.refresh();
      } else if (msg) this.authMsg.set({ ok: false, text: s.error ?? 'Still not connected' });
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
