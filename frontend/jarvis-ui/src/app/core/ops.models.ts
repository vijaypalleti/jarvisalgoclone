export interface AccountListItem {
  clientId: string; name: string; broker: string; accountId: string; status: string; sessionValid: boolean;
  tokenStale: boolean; openPositions: number; closedToday: number; strategies: number; excludedStrategies: number;
  strategyPnl: number; realized: number; unrealized: number;
}

export interface PositionRecord {
  id: string; side: 'BUY' | 'SELL' | 'FLAT'; entrySide: 'BUY' | 'SELL'; exchange: string; symbol: string; displaySymbol: string;
  strategyId: number; strategy: string; product: string; buyQty: number; sellQty: number; netQty: number;
  avgBuy: number; avgSell: number; ltp: number; realized: number; unrealized: number; pnlPct: number | null;
  status: 'OPEN' | 'CLOSED' | 'RISK'; stale: boolean; createdAt: string; closedAt: string | null;
  entrySignalId: string; correlation: string; latestExitId: string | null; token: string; orderTag: string;
}

export interface OrderRecord {
  orderId: string; time: string; symbol: string; side: string; kind: string; qty: number; filledQty: number;
  price: number; status: string; correlation: string; message: string | null;
}

export interface TradeRecord {
  tradeId: string; symbol: string; strategy: string; entrySide: string; exitSide: string; entryQty: number;
  exitQty: number; netQty: number; duration: string; broker: string; accountId: string; entryTime: string;
  exitTime: string | null; pnl: number | null; status: string; correlation: string;
}

export interface InFlightExit {
  id: string; clientId: string; broker: string; accountId: string; symbol: string; qty: number; stage: string;
  attempts: number; since: string; reason: string; correlation: string;
}
export interface CleanupItem { clientId: string; broker: string; accountId: string; symbol: string; issue: string; omsQty: number; brokerQty: number; since: string; }
export interface HedgeRecord {
  clientId: string; broker: string; accountId: string; mainSymbol: string; mainQty: number;
  ceLeg: string; ceQty: number; peLeg: string; peQty: number; status: 'PROTECTED' | 'PARTIAL' | 'NAKED';
}
export interface AutoCloseEntry { time: string; clientId: string; broker: string; accountId: string; symbol: string; qty: number; rule: string; result: string; message: string; }

export interface PositionsTabSummary { inFlightExits: number; needsCleanup: number; hedges: number; autoClosed: number; accountsLoaded: number; }
export interface AccountDetail { account: AccountListItem; positions: PositionRecord[]; orders: OrderRecord[]; }

export interface PerformanceSummary {
  strategyPnl: number; accountsOnPage: number; accountsInScope: number; activeAccounts: number;
  connectedAccounts: number; openPositions: number; users: AccountListItem[];
  scopePnl: number;   // all accounts in scope (strategyPnl is only the accounts on this page)
}
export interface UserPerformanceDetail {
  account: AccountListItem; total: number; realized: number; unrealized: number; closed: number; open: number;
  wins: number; losses: number; markedAt: string; positions: PositionRecord[]; trades: TradeRecord[]; tradesTotal: number;
}

export interface ContainerInfo {
  name: string; status: string; health: string; privateIp: string; staticPublicIp: string; nicMappingValid: boolean;
  routingValid: boolean; clientId: string; broker: string; accounts: string; cpuPct: number; memPct: number; memMb: number;
  redisLatencyMs: number; mongoLatencyMs: number; redisOk: boolean; mongoOk: boolean; ltpFeed: boolean; streams: boolean;
  streamMsgs: number; restarts: number; restartsToday: number; heartbeat: boolean;
}
export interface ContainerSummary {
  total: number; running: number; stopped: number; healthy: number; unhealthy: number; noHeartbeat: number;
  mongoConnected: number; redisConnected: number; routingOk: number; avgCpu: number; avgMemory: number;
  contractMasterRows: number; contractMasterLoadedAt: string; checkedAt: string; containers: ContainerInfo[];
}

export interface TodayActivity {
  hours: { hour: number; signals: number }[];
  funnel: { signals: number; orders: number; filled: number; rejected: number; working: number };
}

// ---------------- Performance → User Management (demo system users)
export interface SystemUserListItem { id: number; name: string; email: string; mobile: string; status: string; hasStaticIp: boolean; accounts: number; }
export interface SystemUserList { total: number; items: SystemUserListItem[]; }
export interface UserDetailsDto {
  id: number; name: string; email: string; mobile: string; role: string; status: string; publicIp: string | null; privateIp: string | null;
  username: string | null; referralCode: string | null; created: string | null; lastModified: string;
}
export interface UserBrokerRow { clientId: string; broker: string; accountId: string; username: string; session: string; lot: number; algoOn: boolean; active: boolean; inOms: boolean; }
export interface UserSubscriptionRow {
  id: number; basket: string; basketStrategies: number; plan: string; months: number; clientId: string; broker: string; multiplier: number;
  start: string; end: string; daysLeft: number; status: string; price: number;
}
export interface BasketStrategyRow { strategy: string; code: string; lotSize: number; mult: number; ce: number; pe: number; product: string; dispatchCe: number | null; dispatchPe: number | null; }
export interface UserBasketView { basket: string; status: string; broker: string; multiplier: number; strategies: BasketStrategyRow[]; }
export interface UserPaymentRow { id: number; subscriptionId: number; basket: string; date: string; amount: number; method: string; status: string; invoice: string; }
export interface UserAttentionItem { severity: 'high' | 'medium' | 'low'; section: 'brokers' | 'subs' | 'billing' | 'user'; text: string; }
export interface UserWorkspace {
  user: UserDetailsDto; brokerAccounts: UserBrokerRow[]; subscriptions: UserSubscriptionRow[]; baskets: UserBasketView[];
  payments: UserPaymentRow[]; lifetimeValue: number; attention: UserAttentionItem[];
}

// ---------------- Basket Management → Basket Subscriptions (all users)
export interface SubscriptionListItem {
  id: number; userId: number; userName: string; basket: string; basketStrategies: number; plan: string; months: number; price: number;
  multiplier: number; start: string; end: string; daysLeft: number; status: string; broker: string; accountId: string; clientId: string;
}
export interface SubscriptionPage {
  total: number; page: number; pageSize: number; pages: number; counts: Record<string, number>; items: SubscriptionListItem[]; brokers: string[];
}
