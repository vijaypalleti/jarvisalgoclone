import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { IconComponent } from '../../shared/icon.component';
import { OpsService } from '../../core/ops.service';
import { ApiService } from '../../core/api.service';
import { SubscriptionListItem, SubscriptionPage, SystemUserListItem, UserBrokerRow, UserWorkspace } from '../../core/ops.models';

type StatusTab = 'all' | 'active' | 'inactive' | 'expired' | 'paused' | 'cancelled';
type Modal =
  | { kind: 'edit'; s: SubscriptionListItem; multiplier: number }
  | { kind: 'extend'; s: SubscriptionListItem; months: number }
  | { kind: 'delete'; s: SubscriptionListItem }
  | { kind: 'create'; search: string; users: SystemUserListItem[]; userId: number | null; userName: string; accounts: UserBrokerRow[];
      clientId: string; basket: string; months: number; multiplier: number };

/**
 * Basket Management → Basket Subscriptions: every subscriber's basket subscriptions in one list, to monitor them and
 * pause / activate / edit the multiplier / extend / remind / delete. Demo data (MockDataStore.Users); changes are in memory only.
 */
@Component({
  selector: 'app-basket-subscriptions',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './basket-subscriptions.component.html',
  styleUrl: './basket-subscriptions.component.scss',
})
export class BasketSubscriptionsComponent {
  private ops = inject(OpsService);

  readonly tabs: { key: StatusTab; label: string; icon: string }[] = [
    { key: 'all', label: 'All Subscriptions', icon: 'list' },
    { key: 'active', label: 'Active', icon: 'check-circle' },
    { key: 'inactive', label: 'Inactive', icon: 'x-circle' },
    { key: 'expired', label: 'Expired', icon: 'alert-triangle' },
    { key: 'paused', label: 'Paused', icon: 'pause' },
    { key: 'cancelled', label: 'Cancelled', icon: 'trash' },
  ];

  // filters (draft → applied)
  filtersOpen = signal(false);
  f = { search: '', basket: '', broker: '', expiring: '' };   // expiring = days ('' = any)
  applied = signal({ ...this.f });
  status = signal<StatusTab>('all');
  page = signal(1);
  pageSize = signal(20);

  data = signal<SubscriptionPage | null>(null);
  loading = signal(false);
  busy = signal<number | null>(null);
  baskets = signal<string[]>([]);
  modal = signal<Modal | null>(null);
  toast = signal<{ text: string; bad?: boolean } | null>(null);
  activeFilters = computed(() => Object.values(this.applied()).filter(Boolean).length);

  pageList = computed(() => {
    const d = this.data(); if (!d) return [];
    const last = d.pages, cur = d.page;
    const start = Math.max(1, Math.min(cur - 2, last - 4)), end = Math.min(last, start + 4);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  });

  constructor() {
    inject(ApiService).signalFilters().subscribe(f => this.baskets.set(f.baskets));
    // filters can come from the URL (?search=&basket=&broker=&expiring=7&status=active), e.g. when the assistant
    // navigates here ("subscriptions expiring within a week", "Mohan Saxena's subscription status")
    inject(ActivatedRoute).queryParamMap.pipe(takeUntilDestroyed()).subscribe(q => {
      const keys = ['search', 'basket', 'broker', 'expiring', 'status'];
      if (!keys.some(k => q.has(k))) return;
      this.f = { search: q.get('search') ?? '', basket: q.get('basket') ?? '', broker: q.get('broker') ?? '', expiring: q.get('expiring') ?? '' };
      this.applied.set({ ...this.f });
      const st = (q.get('status') ?? 'all') as StatusTab;
      this.status.set(this.tabs.some(t => t.key === st) ? st : 'all');
      this.page.set(1);
      this.filtersOpen.set(!!(this.f.search || this.f.basket || this.f.broker || this.f.expiring));
    });
    effect(() => { this.applied(); this.status(); this.page(); this.pageSize(); this.load(); });
  }

  load() {
    const a = this.applied();
    this.loading.set(true);
    this.ops.basketSubscriptions({ status: this.status(), search: a.search, basket: a.basket, broker: a.broker,
      expiringDays: a.expiring ? Number(a.expiring) : null, page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: d => { this.data.set(d); this.loading.set(false); if (d.page !== this.page()) this.page.set(d.page); },
      error: () => { this.loading.set(false); this.flash('Could not load subscriptions', true); },
    });
  }
  apply() { this.applied.set({ ...this.f }); this.page.set(1); }
  reset() { this.f = { search: '', basket: '', broker: '', expiring: '' }; this.apply(); }
  readonly expiringOptions = ['7', '15', '30'];
  setStatus(s: StatusTab) { this.status.set(s); this.page.set(1); }
  setPageSize(n: number) { this.pageSize.set(Number(n)); this.page.set(1); }
  go(p: number) { const d = this.data(); if (d) this.page.set(Math.min(Math.max(1, p), d.pages)); }

  // ------------------------------------------------------------------ row actions (demo)
  private run(call: Observable<UserWorkspace>, s: SubscriptionListItem, done: string) {
    this.busy.set(s.id);
    call.subscribe({
      next: () => { this.busy.set(null); this.modal.set(null); this.flash(done); this.load(); },
      error: e => { this.busy.set(null); this.flash(e?.error?.error ?? 'Change failed', true); },
    });
  }
  toggle(s: SubscriptionListItem) {
    if (s.status === 'Active') this.run(this.ops.subscriptionAction(s.userId, s.id, 'pause'), s, `Paused: ${s.userName} · ${s.basket}`);
    else this.run(this.ops.subscriptionAction(s.userId, s.id, 'activate'), s, `Activated: ${s.userName} · ${s.basket}`);
  }
  remind(s: SubscriptionListItem) {
    this.flash(`Renewal reminder for ${s.userName} · ${s.basket} (demo: no message is actually sent).`);
  }
  newEnd(m: Extract<Modal, { kind: 'extend' }>) {
    const base = new Date(Math.max(new Date(m.s.end).getTime(), new Date().setHours(0, 0, 0, 0)));
    base.setMonth(base.getMonth() + Number(m.months));
    return base;
  }

  // ------------------------------------------------------------------ create
  openCreate() {
    this.modal.set({ kind: 'create', search: '', users: [], userId: null, userName: '', accounts: [], clientId: '', basket: this.baskets()[0] ?? '', months: 3, multiplier: 1 });
    this.searchUsers('');
  }
  private userTimer?: ReturnType<typeof setTimeout>;
  searchUsers(q: string) {
    const m = this.modal(); if (m?.kind !== 'create') return;
    m.search = q;
    clearTimeout(this.userTimer);
    this.userTimer = setTimeout(() => this.ops.users(q).subscribe(r => {
      const cur = this.modal(); if (cur?.kind === 'create') this.modal.set({ ...cur, users: r.items.filter(u => u.accounts > 0).slice(0, 8) });
    }), 200);
  }
  pickUser(u: SystemUserListItem) {
    this.ops.userWorkspace(u.id).subscribe(w => {
      const cur = this.modal(); if (cur?.kind !== 'create') return;
      this.modal.set({ ...cur, userId: u.id, userName: u.name, accounts: w.brokerAccounts, clientId: w.brokerAccounts[0]?.clientId ?? '', users: [] });
    });
  }

  submit() {
    const m = this.modal(); if (!m) return;
    switch (m.kind) {
      case 'edit': {
        const v = Math.trunc(Number(m.multiplier));
        if (!(v >= 1 && v <= 50)) { this.flash('Multiplier must be a whole number from 1 to 50.', true); return; }
        this.run(this.ops.updateSubscription(m.s.userId, m.s.id, v), m.s, `Multiplier ${v}× for ${m.s.userName} · ${m.s.basket}`);
        break;
      }
      case 'extend':
        this.run(this.ops.subscriptionAction(m.s.userId, m.s.id, 'extend', Number(m.months)), m.s, `Extended ${m.s.basket} for ${m.s.userName} by ${m.months} month(s)`);
        break;
      case 'delete':
        this.run(this.ops.deleteSubscription(m.s.userId, m.s.id), m.s, `Deleted ${m.s.basket} subscription of ${m.s.userName}`);
        break;
      case 'create':
        if (!m.userId || !m.clientId || !m.basket) { this.flash('Pick a subscriber, a broker account and a basket.', true); return; }
        this.ops.addSubscription(m.userId, { basket: m.basket, months: Number(m.months), clientId: m.clientId, multiplier: Number(m.multiplier) || 1 }).subscribe({
          next: () => { this.modal.set(null); this.flash(`Created ${m.basket} for ${m.userName}`); this.setStatus('all'); this.load(); },
          error: e => this.flash(e?.error?.error ?? 'Could not create the subscription', true),
        });
        break;
    }
  }

  // ------------------------------------------------------------------ export
  exportCsv() {
    const a = this.applied();
    this.ops.basketSubscriptions({ status: this.status(), search: a.search, basket: a.basket, broker: a.broker,
      expiringDays: a.expiring ? Number(a.expiring) : null, page: 1, pageSize: 5000 }).subscribe(d => {
      const head = ['Subscriber', 'User ID', 'Basket', 'Plan', 'Months', 'Price', 'Multiplier', 'Start', 'End', 'Days left', 'Status', 'Broker', 'Broker account'];
      const esc = (v: unknown) => { const s = String(v ?? ''); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
      const rows = d.items.map(x => [x.userName, x.userId, x.basket, x.plan, x.months, x.price, x.multiplier, x.start.slice(0, 10), x.end.slice(0, 10), x.daysLeft, x.status, x.broker, x.accountId]);
      const blob = new Blob([[head, ...rows].map(r => r.map(esc).join(',')).join('\n')], { type: 'text/csv' });
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = `basket-subscriptions-${this.status()}-${new Date().toISOString().slice(0, 10)}.csv`;
      link.click();
      URL.revokeObjectURL(link.href);
      this.flash(`Exported ${d.items.length} subscription(s)`);
    });
  }

  // ------------------------------------------------------------------ helpers
  private toastTimer?: ReturnType<typeof setTimeout>;
  flash(text: string, bad = false) { this.toast.set({ text, bad }); clearTimeout(this.toastTimer); this.toastTimer = setTimeout(() => this.toast.set(null), 3500); }
  daysText(s: SubscriptionListItem) {
    if (s.status === 'Cancelled') return 'cancelled';
    if (s.daysLeft <= 0) return s.daysLeft === 0 ? 'ends today' : `ended ${-s.daysLeft} day(s) ago`;
    return `${s.daysLeft} days left`;
  }
  ed(m: Modal) { return m as Extract<Modal, { kind: 'edit' }>; }
  ex(m: Modal) { return m as Extract<Modal, { kind: 'extend' }>; }
  de(m: Modal) { return m as Extract<Modal, { kind: 'delete' }>; }
  cr(m: Modal) { return m as Extract<Modal, { kind: 'create' }>; }
}
