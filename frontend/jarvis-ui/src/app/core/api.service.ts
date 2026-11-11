import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  AlertsResponse, BrokerSessionHealth, OverviewSummary, PagedResult, SignalDetailResponse,
  SignalListResponse, SignalQuery, SubscriptionItem
} from './models';

const BASE = '/api/console';

function toParams(obj: object): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(obj)) {
    if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v));
  }
  return p;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  overview() { return this.http.get<OverviewSummary>(`${BASE}/overview`); }

  alerts(search?: string) { return this.http.get<AlertsResponse>(`${BASE}/alerts`, { params: toParams({ search }) }); }
  ackAccount(id: string) { return this.http.post<void>(`${BASE}/alerts/${id}/ack`, {}); }
  ackRejection(id: string) { return this.http.post<void>(`${BASE}/alerts/rejections/${id}/ack`, {}); }
  ackAll() { return this.http.post<void>(`${BASE}/alerts/ack-all`, {}); }

  expiringSubscriptions(page: number, pageSize = 5) {
    return this.http.get<PagedResult<SubscriptionItem>>(`${BASE}/subscriptions/expiring`, { params: toParams({ page, pageSize }) });
  }
  brokerSessions() { return this.http.get<BrokerSessionHealth>(`${BASE}/broker-sessions`); }

  signals(q: SignalQuery) { return this.http.get<SignalListResponse>(`${BASE}/signals`, { params: toParams(q) }); }
  signalFilters() { return this.http.get<{ baskets: string[]; strategies: string[]; strategyCatalog: { name: string; code: string; basket: string }[] }>(`${BASE}/signals/filters`); }
  signalDetail(id: number, q: { client?: string; account?: string; broker?: string; status?: string; page?: number; pageSize?: number }) {
    return this.http.get<SignalDetailResponse>(`${BASE}/signals/${id}`, { params: toParams(q) });
  }
  forceExit(id: number) { return this.http.post<void>(`${BASE}/signals/${id}/force-exit`, {}); }
  exitRemaining(id: number, clientId: string) {
    return this.http.post<void>(`${BASE}/signals/${id}/accounts/${encodeURIComponent(clientId)}/exit-remaining`, {});
  }
  retryChunk(id: number, clientId: string, index: number) {
    return this.http.post<void>(`${BASE}/signals/${id}/accounts/${encodeURIComponent(clientId)}/chunks/${index}/retry`, {});
  }
}
