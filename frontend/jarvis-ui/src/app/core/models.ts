export interface RelayStatus { live: boolean; dispatchPct: number; execPct: number; dlqPct: number; lag: string | null; }

export interface OverviewSummary {
  rangeFrom: string; rangeTo: string;
  deadLettersInRange: number; failedExecutionsInRange: number;
  relay: RelayStatus;
  strategyPerformance: { total: number; profitable: number; losing: number };
  userPerformance: { users: number; accounts: number };
  containers: { running: number; total: number; healthy: number; unhealthy: number };
  inFlightExits: { total: number; pending: number; accounts: number };
  omsSignals: { today: number; inProgress: number; failed: number; accounts: number };
  positionTracking: { total: number; openedToday: number; closedToday: number; loadedAccounts: number };
  logsErrors: { total: number; errors: number; deadLetters: number };
  brokers: { active: number; total: number };
  health: { successRate: number };
  sessions: { valid: number; total: number; notRefreshed: number; noToken: number };
  subscriptions: { ending7d: number; today: number; within3d: number; within30d: number };
}

export interface AlertRejection {
  id: string; time: string; order: string; strategyId: number; status: string;
  reason: string; qty: number; attempts: number; acknowledged: boolean;
}

export interface AlertAccount {
  id: string; broker: string; accountId: string; open: number; total: number;
  symbol: string; reasonCode: string; extraSymbols: number; message: string; lastTime: string;
  rejections: AlertRejection[];
}

export interface AlertsResponse {
  checkedAt: string; openCount: number; criticalCount: number;
  byBroker: { broker: string; open: number; accounts: number; rejections: number }[];
  totalAccounts: number; accounts: AlertAccount[];
}

export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; }

export interface SubscriptionItem { name: string; broker: string; accountId: string; plan: string; endsAt: string; daysLeft: number; }

export interface BrokerSessionHealth {
  liveToday: number; totalSessions: number; excludedDelta: number;
  notRefreshed: { name: string; broker: string; accountId: string; lastRefreshed: string | null }[];
}

export interface HedgeRollup { buyFilled: number; buyRequested: number; sellFilled: number; sellRequested: number; open: number; }

export interface SignalListItem {
  id: number; reachedApiAt: string; symbol: string; exchange: string; segment: string;
  strategyRef: number; strategyName: string; side: string; type: string; status: string;
  exitStatus: string; errors: number; requested: number; filled: number; exited: number;
  rejectedOrUnfilled: number; hedge: HedgeRollup | null; openAccounts: number;
  closedAccounts: number; totalAccounts: number; correlationId: string;
  /** status/exitStatus above are the EFFECTIVE values (worst account wins); this is what the OMS recorded */
  recordedExitStatus: string;
  exitCorrelationId: string | null;
  health: 'ok' | 'open' | 'error' | 'residual';
  needsAttention: boolean;
  residualQty: number; residualAccounts: number; anomalyCount: number;
  chunksDone: number; chunksTotal: number; openQty: number;
  /** rejected/failed/partial accounts + contradictions still needing action (recovered retries excluded) */
  unresolvedIssues: number;
}

export interface SignalListResponse { total: number; openEntrySignals: number; attentionCount: number; items: SignalListItem[]; }

export interface ChunkOrder {
  index: number; taskStatus: string; brokerStatus: string; req: number; fill: number; unfilled: number;
  attempts: number; modify: number | null; brokerOrder: string | null; reason: string | null;
}
export interface AttentionItem { clientId: string; broker: string; brokerAccount: string; issue: string; qty: number; }

export interface ShortOrderLeg {
  label: string; status: string; req: number; fill: number; unfilled: number; attempts: number;
  brokerOrder: string; premiumBought: string; completed: string;
}
export interface HedgeOrderLeg {
  leg: string; side: string; instrument: string; status: string; req: number; fill: number;
  brokerOrder: string; error: string | null;
}
export interface HedgeDetail { summary: string; squared: number; shortOrders: ShortOrderLeg[]; hedgeOrders: HedgeOrderLeg[]; }

export interface SignalAccountExecution {
  clientId: string; broker: string; brokerAccount: string; brokerOrderId: string;
  orderStatus: string; chunks: number | null; req: number; fill: number; pending: number;
  rejected: number; cancelled: number; exit: number; netOpen: number; avgEntry: number;
  avgExit: number | null; retry: number; modify: number | null; rejectReason: string | null;
  attempt: number | null; hedge: HedgeDetail | null;
  chunkOrders: ChunkOrder[]; partialReason: string | null; anomalies: string[]; severity: number;
}

export interface SignalDetailResponse {
  signal: SignalListItem; success: number; partial: number; failed: number; pending: number;
  openAccounts: number; openQty: number; closed: number;
  statusCounts: { status: string; count: number }[];
  attention: AttentionItem[];
  accounts: PagedResult<SignalAccountExecution>; brokers: string[];
}

export interface SignalQuery {
  from?: string; to?: string; symbol?: string; correlationId?: string; client?: string;
  basket?: string; strategy?: string; filter?: string; sort?: string; take?: number;
}
