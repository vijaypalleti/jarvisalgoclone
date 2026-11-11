import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface FyersStatus {
  configured: boolean; connected: boolean; clientId?: string; name?: string; displayName?: string | null;
  fyId?: string; tokenExpiresAt?: string | null; error?: string | null;
}
export interface FyersFund { id: number; title: string; equityAmount: number; commodityAmount: number; }
export interface FyersPosition {
  id: string; symbol: string; productType: string; side: string; netQty: number; netAvg: number;
  buyQty: number; buyAvg: number; sellQty: number; sellAvg: number; ltp: number;
  realizedProfit: number; unrealizedProfit: number; pl: number;
}
export interface FyersPositions {
  overall: { count_open: number; count_total: number; pl_realized: number; pl_unrealized: number; pl_total: number } | null;
  items: FyersPosition[];
}
export interface FyersOrder {
  id: string; exchOrdId: string; symbol: string; qty: number; filledQty: number; remainingQuantity: number;
  status: string; side: string; type: string; productType: string; limitPrice: number; stopPrice: number;
  tradedPrice: number; orderDateTime: string; message: string | null; orderTag: string | null;
}
export interface FyersTrade {
  tradeNumber: string; orderNumber: string; symbol: string; side: string; tradedQty: number;
  tradePrice: number; tradeValue: number; productType: string; orderDateTime: string;
}
export interface FyersHolding { symbol: string; quantity: number; costPrice: number; ltp: number; marketVal: number; pl: number; }
export interface FyersQuote {
  symbol: string; name: string; ltp: number; change: number; changePct: number;
  open: number; high: number; low: number; prevClose: number; volume: number;
}

export interface FyersAuthConfig {
  appId: string | null; secretConfigured: boolean; redirectUri: string; oneClick: boolean;
  saved: { savedUtc: string; accessExpiresAt: string | null; canRenewWithPin: boolean; refreshExpiresAt: string | null } | null;
}

const BASE = '/api/fyers';

/** Read-only live Fyers data via the .NET API (the token never reaches the browser). */
@Injectable({ providedIn: 'root' })
export class FyersService {
  private http = inject(HttpClient);
  status() { return this.http.get<FyersStatus>(`${BASE}/status`); }
  funds() { return this.http.get<FyersFund[]>(`${BASE}/funds`); }
  positions() { return this.http.get<FyersPositions>(`${BASE}/positions`); }
  orders() { return this.http.get<FyersOrder[]>(`${BASE}/orders`); }
  trades() { return this.http.get<FyersTrade[]>(`${BASE}/trades`); }
  holdings() { return this.http.get<FyersHolding[]>(`${BASE}/holdings`); }
  quotes(symbols?: string) { return this.http.get<FyersQuote[]>(`${BASE}/quotes`, { params: symbols ? { symbols } : {} }); }
  setToken(clientId: string, accessToken: string) { return this.http.post<void>(`${BASE}/token`, { clientId, accessToken }); }

  // in-app login (replaces the Python session script); secret + tokens stay in the API
  authConfig() { return this.http.get<FyersAuthConfig>(`${BASE}/auth/config`); }
  authStart() { return this.http.get<{ url: string; oneClick: boolean; redirectUri: string }>(`${BASE}/auth/start`); }
  authExchange(input: string) { return this.http.post<{ message: string }>(`${BASE}/auth/exchange`, { input }); }
  authRenew(pin: string) { return this.http.post<{ message: string }>(`${BASE}/auth/renew`, { pin }); }
}
