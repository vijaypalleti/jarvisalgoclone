import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { OpsService } from '../../core/ops.service';
import { ApiService } from '../../core/api.service';
import { UserBrokerRow, UserPaymentRow, UserSubscriptionRow, UserWorkspace } from '../../core/ops.models';

export type UserModal =
  | { kind: 'user'; mode: 'create' | 'edit'; name: string; email: string; mobile: string }
  | { kind: 'number'; title: string; label: string; value: number; min: number; max: number; apply: (v: number) => Observable<UserWorkspace> }
  | { kind: 'subscribe'; basket: string; months: number; clientId: string; multiplier: number }
  | { kind: 'confirm'; title: string; text: string; danger: boolean; apply: () => Observable<UserWorkspace> };

/**
 * The user selected in Performance → User Performance. It stays selected across the sub-tabs (Overview, Trading,
 * User Management, Basket & Strategy, Subscriptions & Billing) and scopes each of them, like the production console.
 * Also holds the demo actions, the dialog and the toast, so a change on one tab shows on every tab.
 */
@Injectable({ providedIn: 'root' })
export class UserWorkspaceService {
  private ops = inject(OpsService);

  readonly ws = signal<UserWorkspace | null>(null);
  /** ACCOUNTS filter in the tab row: '' = all of the user's broker accounts, else one OMS client id */
  readonly accFilter = signal('');
  readonly busy = signal(false);
  readonly toast = signal<{ text: string; bad?: boolean } | null>(null);
  readonly modal = signal<UserModal | null>(null);
  readonly basketNames = signal<string[]>([]);
  /** bumps when users are created, so the picker reloads */
  readonly listVersion = signal(0);

  readonly user = computed(() => this.ws()?.user ?? null);
  /** OMS client ids in scope (for the Trading filter): the picked account, or all of the user's accounts */
  readonly clientIds = computed(() => {
    const w = this.ws(); if (!w) return null;
    const f = this.accFilter();
    return f ? [f] : w.brokerAccounts.map(a => a.clientId);
  });
  readonly brokerRows = computed(() => this.byAcc(this.ws()?.brokerAccounts ?? []));
  readonly subRows = computed(() => this.byAcc(this.ws()?.subscriptions ?? []));
  readonly basketRows = computed(() => {
    const w = this.ws(); if (!w) return [];
    const f = this.accFilter(); if (!f) return w.baskets;
    const mine = new Set(w.subscriptions.filter(s => s.clientId === f && s.status !== 'Cancelled').map(s => s.basket));
    return w.baskets.filter(b => mine.has(b.basket));
  });
  readonly payRows = computed<UserPaymentRow[]>(() => {
    const w = this.ws(); if (!w) return [];
    const f = this.accFilter(); if (!f) return w.payments;
    const subs = new Set(w.subscriptions.filter(s => s.clientId === f).map(s => s.id));
    return w.payments.filter(p => subs.has(p.subscriptionId));
  });

  constructor() { inject(ApiService).signalFilters().subscribe(f => this.basketNames.set(f.baskets)); }

  // ------------------------------------------------------------------ selection
  open(id: number) {
    if (this.ws()?.user.id !== id) this.accFilter.set('');
    this.ops.userWorkspace(id).subscribe({ next: w => this.ws.set(w), error: () => this.flash('User not found', true) });
  }
  close() { this.ws.set(null); this.accFilter.set(''); }
  refresh() { const w = this.ws(); if (w) this.open(w.user.id); }
  /** Trading → ⋮ → Manage User */
  openByClient(clientId: string, then?: () => void) {
    this.ops.userByClient(clientId).subscribe({ next: r => { this.open(r.id); then?.(); }, error: () => this.flash('No user owns this account', true) });
  }

  // ------------------------------------------------------------------ actions (demo: in-memory store only)
  private get id() { return this.ws()!.user.id; }
  run(call: Observable<UserWorkspace>, done: string) {
    this.busy.set(true);
    call.subscribe({
      next: w => { this.ws.set(w); this.busy.set(false); this.modal.set(null); this.flash(done); },
      error: e => { this.busy.set(false); this.flash(e?.error?.error ?? 'Change failed', true); },
    });
  }
  demoOnly(what: string) { this.flash(`${what} needs a real broker or the global configuration, so it isn't available in the demo.`, true); }

  editUser() { const u = this.ws()!.user; this.modal.set({ kind: 'user', mode: 'edit', name: u.name, email: u.email, mobile: u.mobile }); }
  createUser() { this.modal.set({ kind: 'user', mode: 'create', name: '', email: '', mobile: '' }); }
  toggleUserStatus() {
    const u = this.ws()!.user; const active = u.status !== 'Active';
    this.modal.set({ kind: 'confirm', title: active ? 'Activate user' : 'Deactivate user', danger: !active,
      text: active ? `Activate ${u.name}?` : `Deactivate ${u.name}? They can no longer log in, and their broker accounts stop receiving orders.`,
      apply: () => this.ops.setUserStatus(u.id, active) });
  }
  setAlgo(a: UserBrokerRow) { this.run(this.ops.updateUserAccount(this.id, a.clientId, { algoOn: !a.algoOn }), `Algo ${a.algoOn ? 'off' : 'on'} for ${a.accountId}`); }
  setLot(a: UserBrokerRow) {
    const id = this.id;
    this.modal.set({ kind: 'number', title: `Lot for ${a.broker} · ${a.accountId}`, label: 'Lot (0 = use the basket setting)', value: a.lot, min: 0, max: 100,
      apply: v => this.ops.updateUserAccount(id, a.clientId, { lot: v }) });
  }
  toggleAccount(a: UserBrokerRow) {
    const id = this.id;
    this.modal.set({ kind: 'confirm', title: a.active ? 'Deactivate broker account' : 'Activate broker account', danger: a.active,
      text: `${a.active ? 'Deactivate' : 'Activate'} ${a.broker} · ${a.accountId}?`, apply: () => this.ops.updateUserAccount(id, a.clientId, { active: !a.active }) });
  }
  subAction(s: UserSubscriptionRow, action: 'pause' | 'activate' | 'cancel' | 'extend') {
    const id = this.id;
    if (action === 'cancel') {
      this.modal.set({ kind: 'confirm', title: 'Cancel subscription', danger: true, text: `Cancel the ${s.basket} subscription on ${s.broker}?`,
        apply: () => this.ops.subscriptionAction(id, s.id, 'cancel') });
      return;
    }
    const label = { pause: 'Paused', activate: 'Activated', extend: 'Extended by 1 month' }[action];
    this.run(this.ops.subscriptionAction(id, s.id, action), `${label}: ${s.basket}`);
  }
  editSub(s: UserSubscriptionRow) {
    const id = this.id;
    this.modal.set({ kind: 'number', title: `${s.basket} · ${s.broker}`, label: 'Multiplier for this broker account', value: s.multiplier, min: 1, max: 50,
      apply: v => this.ops.updateSubscription(id, s.id, v) });
  }
  changeBasketMult(basket: string, current: number) {
    const id = this.id;
    this.modal.set({ kind: 'number', title: `This user's multiplier · ${basket}`, label: "Multiplier (applies to all of this user's subscriptions to the basket)", value: current, min: 1, max: 50,
      apply: v => this.ops.setBasketMultiplier(id, basket, v) });
  }
  addSub(basket?: string, clientId?: string) {
    const acc = clientId ?? this.brokerRows()[0]?.clientId;
    if (!acc) { this.flash('Add a broker account first.', true); return; }
    this.modal.set({ kind: 'subscribe', basket: basket ?? this.basketNames()[0] ?? '', months: 3, clientId: acc, multiplier: 1 });
  }
  markPaid(p: UserPaymentRow) { this.run(this.ops.markPaymentPaid(this.id, p.id), `${p.invoice} marked as paid`); }

  submitModal() {
    const m = this.modal(); if (!m) return;
    switch (m.kind) {
      case 'user':
        if (!m.name.trim() || !m.email.trim()) { this.flash('Name and email are required.', true); return; }
        if (m.mode === 'edit') this.run(this.ops.updateUser(this.id, m), 'User updated');
        else this.ops.createUser(m).subscribe({
          next: r => { this.modal.set(null); this.listVersion.update(v => v + 1); this.open(r.id); this.flash('User created'); },
          error: e => this.flash(e?.error?.error ?? 'Could not create user', true),
        });
        break;
      case 'number': {
        const v = Math.trunc(Number(m.value));
        if (!Number.isFinite(v) || v < m.min || v > m.max) { this.flash(`Enter a whole number from ${m.min} to ${m.max}.`, true); return; }
        this.run(m.apply(v), 'Saved');
        break;
      }
      case 'subscribe':
        this.run(this.ops.addSubscription(this.id, { basket: m.basket, months: Number(m.months), clientId: m.clientId, multiplier: Number(m.multiplier) }), `Subscribed to ${m.basket}`);
        break;
      case 'confirm':
        this.run(m.apply(), 'Done');
        break;
    }
  }

  // ------------------------------------------------------------------ helpers
  private byAcc<T extends { clientId: string }>(rows: T[]) { const f = this.accFilter(); return f ? rows.filter(r => r.clientId === f) : rows; }
  private toastTimer?: ReturnType<typeof setTimeout>;
  flash(text: string, bad = false) { this.toast.set({ text, bad }); clearTimeout(this.toastTimer); this.toastTimer = setTimeout(() => this.toast.set(null), 3500); }
  accLabel(clientId: string) { const a = this.ws()?.brokerAccounts.find(x => x.clientId === clientId); return a ? `${a.broker} · ${a.accountId}` : clientId; }
}

export const initials = (n: string) => n.split(/\s+/).filter(Boolean).map(p => p[0]).join('').slice(0, 2).toUpperCase();
export const plural = (n: number, w: string) => `${n} ${n === 1 ? w : w.endsWith('y') ? w.slice(0, -1) + 'ies' : w + 's'}`;
/** Same rule as the backend (MockDataStore.Dispatch): mult × lot × strategy mult × leg, truncated; 0 = unset → 1; leg 0 = not traded. */
export const dispatchQty = (mult: number, lot: number, sm: number, leg: number) => leg <= 0 ? null : Math.trunc((mult || 1) * (lot || 1) * (sm || 1) * leg);
