import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  AccountDetail, AccountListItem, AutoCloseEntry, CleanupItem, ContainerSummary, HedgeRecord, InFlightExit,
  PerformanceSummary, PositionsTabSummary, TodayActivity, UserPerformanceDetail
} from './ops.models';

const BASE = '/api/console';

function params(obj: object): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(obj)) if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v));
  return p;
}

/** Positions & Exits, Performance, Infrastructure and Overview activity endpoints. */
@Injectable({ providedIn: 'root' })
export class OpsService {
  private http = inject(HttpClient);

  todayActivity() { return this.http.get<TodayActivity>(`${BASE}/activity/today`); }

  // Positions & Exits
  positionsSummary() { return this.http.get<PositionsTabSummary>(`${BASE}/positions/summary`); }
  accounts(q: { search?: string; broker?: string; clientId?: string; partnerId?: string; openOnly?: boolean; sort?: string }) {
    return this.http.get<AccountListItem[]>(`${BASE}/positions/accounts`, { params: params(q) });
  }
  account(clientId: string, q: { status?: string; symbol?: string; strategyId?: string; correlation?: string; product?: string }) {
    return this.http.get<AccountDetail>(`${BASE}/positions/accounts/${encodeURIComponent(clientId)}`, { params: params(q) });
  }
  exitPosition(clientId: string, positionId: string) {
    return this.http.post<void>(`${BASE}/positions/accounts/${encodeURIComponent(clientId)}/${encodeURIComponent(positionId)}/exit`, {});
  }
  inFlight() { return this.http.get<InFlightExit[]>(`${BASE}/positions/inflight`); }
  cleanup() { return this.http.get<CleanupItem[]>(`${BASE}/positions/cleanup`); }
  hedges() { return this.http.get<HedgeRecord[]>(`${BASE}/positions/hedges`); }
  autoClose() { return this.http.get<AutoCloseEntry[]>(`${BASE}/positions/auto-close`); }

  // Performance
  /** strategies: comma-separated names; P&L / positions are then computed from those strategies only */
  performance(q: { basket?: string; partnerId?: string; name?: string; broker?: string; sort?: string; strategies?: string }) {
    return this.http.get<PerformanceSummary>(`${BASE}/performance/summary`, { params: params(q) });
  }
  userPerformance(clientId: string, strategies?: string) {
    return this.http.get<UserPerformanceDetail>(`${BASE}/performance/users/${encodeURIComponent(clientId)}`, { params: params({ strategies }) });
  }

  // Infrastructure
  containers() { return this.http.get<ContainerSummary>(`${BASE}/infrastructure/containers`); }
  restartContainer(name: string) { return this.http.post<void>(`${BASE}/infrastructure/containers/${name}/restart`, {}); }
  stopContainer(name: string) { return this.http.post<void>(`${BASE}/infrastructure/containers/${name}/stop`, {}); }
  provisionAll() { return this.http.post<void>(`${BASE}/infrastructure/containers/provision-all`, {}); }
  stopAll() { return this.http.post<void>(`${BASE}/infrastructure/containers/stop-all`, {}); }
  reloadContractMaster() { return this.http.post<void>(`${BASE}/infrastructure/contract-master/reload`, {}); }
}
