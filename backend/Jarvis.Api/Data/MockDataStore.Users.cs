using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

// ======================================================================= User Management (Performance → User Management)
// System users OWN broker accounts, basket subscriptions and baskets. Every demo OMS broker account belongs to a user
// (accounts with the same person's name share one user); extra users without OMS accounts make the picker realistic.
// All data is fake (example.com emails, 90000-xxxxx mobiles). Mutations change only this in-memory demo store.

public class SystemUser
{
    public int Id { get; init; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Mobile { get; set; }
    public string Role { get; init; } = "Role 3";
    public string Status { get; set; } = "Active";
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? Username { get; init; }
    public string? ReferralCode { get; init; }
    public DateTime? Created { get; init; }
    public DateTime LastModified { get; set; }
    public List<UserBrokerLink> Accounts { get; } = new();
    public List<UserSubscription> Subscriptions { get; } = new();
    public List<UserPayment> Payments { get; } = new();
}

public class UserPayment
{
    public int Id { get; init; }
    public int SubscriptionId { get; init; }
    public required string Basket { get; init; }
    public DateTime Date { get; init; }
    public decimal Amount { get; init; }
    public required string Method { get; init; }        // UPI / Card / NetBanking
    public string Status { get; set; } = "Paid";        // Paid / Pending / Failed
    public required string Invoice { get; init; }
}

public class UserBrokerLink
{
    public required string ClientId { get; init; }      // OMS client id (links to BrokerAccount when it exists)
    public required string Broker { get; init; }
    public required string AccountId { get; init; }     // the broker's client code
    public required string Username { get; init; }
    public int Lot { get; set; }
    public bool AlgoOn { get; set; } = true;
    public bool Active { get; set; } = true;
    public bool LoggedInFallback { get; set; }          // for accounts that are not in the OMS demo book
    public DateTime? LastLogin { get; set; }
}

public class UserSubscription
{
    public int Id { get; init; }
    public required string Basket { get; init; }
    public required string Plan { get; init; }
    public int Months { get; init; }
    public required string ClientId { get; init; }
    public required string Broker { get; init; }
    public int Multiplier { get; set; } = 1;
    public DateTime Start { get; init; }
    public DateTime End { get; set; }
    public string Status { get; set; } = "Active";      // Active / Inactive / Paused / Cancelled
}

/// <summary>Basket-wide strategy settings (shared by every subscriber).</summary>
public record BasketStrategyConfig(string Strategy, string Code, int LotSize, int Mult, int Ce, int Pe, string Product);

// ---------- API shapes
public record SystemUserListItem(int Id, string Name, string Email, string Mobile, string Status, bool HasStaticIp, int Accounts);
public record SystemUserList(int Total, List<SystemUserListItem> Items);
public record UserDetailsDto(int Id, string Name, string Email, string Mobile, string Role, string Status, string? PublicIp, string? PrivateIp,
    string? Username, string? ReferralCode, DateTime? Created, DateTime LastModified);
public record UserBrokerRow(string ClientId, string Broker, string AccountId, string Username, string Session, int Lot, bool AlgoOn, bool Active, bool InOms);
public record UserSubscriptionRow(int Id, string Basket, int BasketStrategies, string Plan, int Months, string ClientId, string Broker, int Multiplier,
    DateTime Start, DateTime End, int DaysLeft, string Status, decimal Price);
public record BasketStrategyRow(string Strategy, string Code, int LotSize, int Mult, int Ce, int Pe, string Product, int? DispatchCe, int? DispatchPe);
public record UserBasketView(string Basket, string Status, string Broker, int Multiplier, List<BasketStrategyRow> Strategies);
public record SubscriptionListItem(int Id, int UserId, string UserName, string Basket, int BasketStrategies, string Plan, int Months, decimal Price,
    int Multiplier, DateTime Start, DateTime End, int DaysLeft, string Status, string Broker, string AccountId, string ClientId);
public record SubscriptionPage(int Total, int Page, int PageSize, int Pages, Dictionary<string, int> Counts, List<SubscriptionListItem> Items, List<string> Brokers);
public record UserPaymentRow(int Id, int SubscriptionId, string Basket, DateTime Date, decimal Amount, string Method, string Status, string Invoice);
/// <summary>Overview → Needs attention. Severity: high / medium / low. Section: brokers / subs / billing / user.</summary>
public record UserAttentionItem(string Severity, string Section, string Text);
public record UserWorkspace(UserDetailsDto User, List<UserBrokerRow> BrokerAccounts, List<UserSubscriptionRow> Subscriptions, List<UserBasketView> Baskets,
    List<UserPaymentRow> Payments, decimal LifetimeValue, List<UserAttentionItem> Attention);

public partial class MockDataStore
{
    public List<SystemUser> Users { get; } = new();
    private readonly Dictionary<string, BasketStrategyConfig> _basketCfg = new();
    private int _nextUserId, _nextSubId = 5001, _nextPayId = 1;

    private static readonly string[] ExtraFirst = ["Amit", "Neha", "Suresh", "Pooja", "Manish", "Ritu", "Deepak", "Swati", "Gaurav", "Anjali", "Varun", "Kiran", "Harish", "Shruti", "Mohan", "Tanvi", "Ravi", "Isha", "Sanjay", "Nisha"];
    private static readonly string[] ExtraLast = ["Patel", "Singh", "Desai", "Mehta", "Chopra", "Bhat", "Pillai", "Saxena", "Agarwal", "Kapoor", "Mishra", "Shetty"];
    private static readonly int[] PlanMonths = [1, 3, 3, 6, 12];

    /// <summary>Seeded last with its own Random, so the existing demo data (signals, accounts, P&L) is unchanged.</summary>
    private void SeedUsers()
    {
        var r = new Random(7);
        var today = NowIst.Date;
        string Pk(string[] a) => a[r.Next(a.Length)];

        foreach (var s in Catalog.Strategies)
            _basketCfg[s.Name] = new BasketStrategyConfig(s.Name, s.Code, r.Next(0, 10) < 7 ? 1 : r.Next(2, 4), r.Next(0, 10) < 8 ? 1 : 2,
                r.Next(0, 10) < 8 ? 1 : 0, r.Next(0, 10) < 8 ? 1 : 2, r.Next(0, 10) < 7 ? "NRML" : "MIS");

        var id = 8600;
        SystemUser NewUser(string name, string? clientIdForIp)
        {
            var c = clientIdForIp is null ? null : Containers.FirstOrDefault(x => x.ClientId == clientIdForIp);
            var slug = name.ToLowerInvariant().Replace(' ', '.');
            var u = new SystemUser
            {
                Id = ++id, Name = name, Email = $"{slug}{r.Next(1, 99)}@example.com", Mobile = $"90000{r.Next(10000, 99999)}",
                Status = r.Next(0, 25) == 0 ? "Inactive" : "Active",
                PublicIp = c?.StaticPublicIp ?? (r.Next(0, 4) == 0 ? null : $"183.{r.Next(10, 250)}.{r.Next(10, 250)}.{r.Next(2, 250)}"),
                PrivateIp = c?.PrivateIp ?? $"192.168.{r.Next(1, 90)}.{r.Next(2, 250)}",
                Username = r.Next(0, 3) == 0 ? slug.Replace(".", "") : null,
                ReferralCode = r.Next(0, 5) == 0 ? $"JRV{r.Next(1000, 9999)}" : null,
                Created = r.Next(0, 3) == 0 ? null : today.AddDays(-r.Next(30, 700)),
                LastModified = today.AddDays(-r.Next(0, 30)).AddHours(r.Next(9, 18)).AddMinutes(r.Next(60)),
            };
            Users.Add(u);
            return u;
        }
        void AddSub(SystemUser u, UserBrokerLink l, string basket)
        {
            var months = PlanMonths[r.Next(PlanMonths.Length)];
            var roll = r.Next(0, 20);                    // 0-11 active, 12-14 inactive, 15-16 paused, 17 cancelled, 18-19 expired
            var start = roll >= 18 ? today.AddMonths(-months).AddDays(-r.Next(1, 45)) : today.AddDays(-r.Next(0, months * 30 - 1));
            u.Subscriptions.Add(new UserSubscription
            {
                Id = _nextSubId++, Basket = basket, Plan = basket.ToLowerInvariant().Replace(' ', '_'), Months = months,
                ClientId = l.ClientId, Broker = l.Broker, Multiplier = r.Next(0, 10) < 7 ? 1 : r.Next(2, 4),
                Start = start, End = start.AddMonths(months),
                Status = roll < 12 || roll >= 18 ? "Active" : roll < 15 ? "Inactive" : roll < 17 ? "Paused" : "Cancelled",
            });
            var sub = u.Subscriptions[^1];
            var pay = r.Next(0, 10);                     // ~30 % trials with no payment yet; the rest paid, a few pending / failed
            if (pay >= 3)
                u.Payments.Add(new UserPayment
                {
                    Id = _nextPayId++, SubscriptionId = sub.Id, Basket = basket, Date = start.AddHours(r.Next(9, 20)).AddMinutes(r.Next(60)),
                    Amount = PlanPrice(months), Method = Pk(["UPI", "UPI", "Card", "NetBanking"]),
                    Status = pay == 9 ? "Failed" : pay == 8 ? "Pending" : "Paid", Invoice = $"INV-{start:yyMM}-{_nextPayId:D5}",
                });
        }

        // owners of the demo OMS accounts (same name → same person)
        Users.Add(new SystemUser { Id = 8600, Name = "Admin Algo", Email = "admin@example.com", Mobile = "9000000001", Role = "Role 1",
            LastModified = today.AddHours(10), Created = today.AddYears(-2), PublicIp = "183.82.10.20", PrivateIp = "192.168.1.10" });
        foreach (var g in Accounts.GroupBy(a => a.Name))
        {
            var u = NewUser(g.Key, g.First().ClientId);
            foreach (var a in g)
            {
                var l = new UserBrokerLink { ClientId = a.ClientId, Broker = a.Broker, AccountId = a.AccountId, Username = a.Name, Lot = r.Next(0, 3) };
                u.Accounts.Add(l);
                AddSub(u, l, a.Basket);
                if (r.Next(0, 3) == 0) AddSub(u, l, Baskets[r.Next(Baskets.Length)]);
            }
        }
        // people without OMS accounts (some have a broker account that isn't trading in the demo book)
        for (var i = 0; i < 120; i++)
        {
            var u = NewUser($"{Pk(ExtraFirst)} {Pk(ExtraLast)}", null);
            if (r.Next(0, 10) < 6)
            {
                var broker = OpsBrokers[r.Next(OpsBrokers.Length)];
                var l = new UserBrokerLink { ClientId = $"U{u.Id}", Broker = broker, AccountId = $"{broker[..2]}{r.Next(10000, 99999)}", Username = u.Name,
                    Lot = r.Next(0, 2), AlgoOn = r.Next(0, 3) > 0, LoggedInFallback = r.Next(0, 2) == 0 };
                u.Accounts.Add(l);
                AddSub(u, l, Baskets[r.Next(Baskets.Length)]);
            }
        }
        _nextUserId = id;
    }

    // ------------------------------------------------------------------ queries

    public SystemUserList ListUsers(string? search)
    {
        lock (_lock)
        {
            IEnumerable<SystemUser> q = Users;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(u => u.Name.Contains(s, StringComparison.OrdinalIgnoreCase) || u.Email.Contains(s, StringComparison.OrdinalIgnoreCase)
                              || u.Mobile.Contains(s) || u.Id.ToString() == s.TrimStart('#'));
            }
            var items = q.Select(u => new SystemUserListItem(u.Id, u.Name, u.Email, u.Mobile, u.Status, u.PublicIp is not null, u.Accounts.Count)).ToList();
            return new SystemUserList(Users.Count, items);
        }
    }

    /// <summary>Trading → ⋮ → Manage User: the system user that owns this OMS client id.</summary>
    public int? UserIdForClient(string clientId)
    {
        lock (_lock) return Users.FirstOrDefault(u => u.Accounts.Any(a => a.ClientId == clientId))?.Id;
    }

    public UserWorkspace? GetUserWorkspace(int id)
    {
        lock (_lock)
        {
            var u = Users.FirstOrDefault(x => x.Id == id);
            return u is null ? null : Workspace(u);
        }
    }

    private UserWorkspace Workspace(SystemUser u)
    {
        var today = NowIst.Date;
        var details = new UserDetailsDto(u.Id, u.Name, u.Email, u.Mobile, u.Role, u.Status, u.PublicIp, u.PrivateIp, u.Username, u.ReferralCode, u.Created, u.LastModified);
        var accounts = u.Accounts.Select(l =>
        {
            var oms = Accounts.FirstOrDefault(a => a.ClientId == l.ClientId);
            var loggedIn = oms?.SessionValid ?? l.LoggedInFallback;
            return new UserBrokerRow(l.ClientId, l.Broker, l.AccountId, l.Username, loggedIn ? "Logged in" : "Logged out", l.Lot, l.AlgoOn, l.Active, oms is not null);
        }).ToList();
        var subs = u.Subscriptions.Select(s =>
        {
            var left = Math.Max(0, (s.End.Date - today).Days);
            var status = s.Status == "Active" && left == 0 ? "Expired" : s.Status;
            return new UserSubscriptionRow(s.Id, s.Basket, Catalog.Strategies.Count(x => x.Basket == s.Basket), s.Plan, s.Months, s.ClientId, s.Broker,
                s.Multiplier, s.Start, s.End, left, status, PlanPrice(s.Months));
        }).ToList();
        var baskets = u.Subscriptions.Where(s => s.Status != "Cancelled").GroupBy(s => s.Basket).Select(g =>
        {
            var sub = g.OrderByDescending(s => s.Status == "Active").First();
            var mult = sub.Multiplier;
            var rows = Catalog.Strategies.Where(x => x.Basket == g.Key).Select(x =>
            {
                var c = _basketCfg[x.Name];
                return new BasketStrategyRow(c.Strategy, c.Code, c.LotSize, c.Mult, c.Ce, c.Pe, c.Product, Dispatch(mult, c.LotSize, c.Mult, c.Ce), Dispatch(mult, c.LotSize, c.Mult, c.Pe));
            }).ToList();
            var basketActive = Accounts.Any() && g.Any(s => s.Status == "Active");
            return new UserBasketView(g.Key, basketActive ? "Active" : "Inactive", sub.Broker, mult, rows);
        }).ToList();
        var payments = u.Payments.OrderByDescending(p => p.Date)
            .Select(p => new UserPaymentRow(p.Id, p.SubscriptionId, p.Basket, p.Date, p.Amount, p.Method, p.Status, p.Invoice)).ToList();
        return new UserWorkspace(details, accounts, subs, baskets, payments, u.Payments.Where(p => p.Status == "Paid").Sum(p => p.Amount),
            Attention(u, accounts, subs, payments));
    }

    /// <summary>Price of a basket plan (demo): Rs 2,499 per month, 10 % off for 6+ months, 20 % off for 12.</summary>
    public static decimal PlanPrice(int months) => Math.Round(2499m * months * (months >= 12 ? 0.8m : months >= 6 ? 0.9m : 1m), 0);

    /// <summary>Everything about this client that needs a human, across all sections (Overview → Needs attention).</summary>
    private static List<UserAttentionItem> Attention(SystemUser u, List<UserBrokerRow> accounts, List<UserSubscriptionRow> subs, List<UserPaymentRow> payments)
    {
        var list = new List<UserAttentionItem>();
        if (u.Status != "Active") list.Add(new("high", "user", "User is inactive: they can't log in and their accounts receive no orders."));
        if (u.PublicIp is null) list.Add(new("medium", "user", "No static IP configured: broker orders need a whitelisted static IP."));
        if (accounts.Count == 0) list.Add(new("medium", "brokers", "No broker account linked."));
        foreach (var a in accounts)
        {
            if (!a.Active) list.Add(new("medium", "brokers", $"{a.Broker} {a.AccountId}: broker account is deactivated."));
            else if (a.Session != "Logged in") list.Add(new("high", "brokers", $"{a.Broker} {a.AccountId}: not logged in today, so orders will fail."));
            if (a.Active && !a.AlgoOn) list.Add(new("medium", "brokers", $"{a.Broker} {a.AccountId}: algo is off, so no signals are traded."));
        }
        foreach (var s in subs)
        {
            if (s.Status == "Active" && s.DaysLeft <= 15) list.Add(new(s.DaysLeft <= 3 ? "high" : "medium", "subs", $"{s.Basket} on {s.Broker}: expires in {s.DaysLeft} day(s)."));
            if (s.Status == "Expired") list.Add(new("high", "subs", $"{s.Basket} on {s.Broker}: subscription expired."));
            if (s.Status == "Paused") list.Add(new("low", "subs", $"{s.Basket} on {s.Broker}: subscription is paused."));
        }
        foreach (var p in payments.Where(p => p.Status != "Paid"))
            list.Add(new(p.Status == "Failed" ? "high" : "medium", "billing", $"Payment {p.Invoice} ({p.Basket}, {Rupees(p.Amount)}) is {p.Status.ToLowerInvariant()}."));
        return list.OrderBy(x => x.Severity switch { "high" => 0, "medium" => 1, _ => 2 }).ToList();
    }
    private static string Rupees(decimal v) => "₹" + v.ToString("#,##,##0", new System.Globalization.CultureInfo("en-IN"));

    public UserWorkspace? MarkPaymentPaid(int id, int paymentId) => Mutate(id, u =>
    {
        var p = u.Payments.FirstOrDefault(x => x.Id == paymentId);
        if (p is null || p.Status == "Paid") return false;
        p.Status = "Paid"; return true;
    });

    /// <summary>
    /// Qty the OMS sends for one leg: userMultiplier × (lotSize × strategyMultiplier) × leg factor, truncated (2.9 → 2).
    /// A stored 0 in the multiplier chain means "unset" and falls back to 1; a leg factor of 0 means the leg isn't traded.
    /// </summary>
    public static int? Dispatch(decimal userMult, int lotSize, int stratMult, int leg)
    {
        if (leg <= 0) return null;
        static decimal Or1(decimal v) => v == 0 ? 1 : v;
        return (int)Math.Truncate(Or1(userMult) * Or1(lotSize) * Or1(stratMult) * leg);
    }

    // ------------------------------------------------------------------ demo mutations (in memory only)

    private UserWorkspace? Mutate(int id, Func<SystemUser, bool> change)
    {
        lock (_lock)
        {
            var u = Users.FirstOrDefault(x => x.Id == id);
            if (u is null || !change(u)) return null;
            u.LastModified = NowIst;
            return Workspace(u);
        }
    }

    public UserWorkspace? SetUserStatus(int id, bool active) => Mutate(id, u => { u.Status = active ? "Active" : "Inactive"; return true; });

    public UserWorkspace? UpdateUser(int id, string name, string email, string mobile) => Mutate(id, u =>
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)) return false;
        u.Name = name.Trim(); u.Email = email.Trim(); u.Mobile = mobile.Trim(); return true;
    });

    public int? CreateUser(string name, string email, string mobile)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email)) return null;
        lock (_lock)
        {
            var u = new SystemUser { Id = ++_nextUserId, Name = name.Trim(), Email = email.Trim(), Mobile = mobile.Trim(), Created = NowIst, LastModified = NowIst };
            Users.Insert(1, u);
            return u.Id;
        }
    }

    public UserWorkspace? UpdateBrokerAccount(int id, string clientId, bool? algoOn, int? lot, bool? active) => Mutate(id, u =>
    {
        var l = u.Accounts.FirstOrDefault(a => a.ClientId == clientId);
        if (l is null || lot is < 0 or > 100) return false;    // invalid lot → 400, not a silent no-op
        if (algoOn is { } a) l.AlgoOn = a;
        if (lot is { } n) l.Lot = n;
        if (active is { } act) l.Active = act;
        return true;
    });

    public UserWorkspace? SubscriptionAction(int id, int subId, string action, int months = 1) => Mutate(id, u =>
    {
        var s = u.Subscriptions.FirstOrDefault(x => x.Id == subId);
        if (s is null) return false;
        switch (action)
        {
            case "pause": s.Status = "Paused"; break;
            case "activate": s.Status = "Active"; if (s.End.Date <= NowIst.Date) s.End = NowIst.Date.AddMonths(s.Months); break;
            case "cancel": s.Status = "Cancelled"; break;
            case "extend":
                if (months is < 1 or > 24) return false;
                s.End = (s.End.Date < NowIst.Date ? NowIst.Date : s.End).AddMonths(months);
                if (s.Status == "Cancelled") return false;
                break;
            default: return false;
        }
        return true;
    });

    public UserWorkspace? DeleteSubscription(int id, int subId) => Mutate(id, u => u.Subscriptions.RemoveAll(x => x.Id == subId) > 0);

    /// <summary>Active subscriptions ending within <paramref name="days"/> days (at least tomorrow), soonest first.</summary>
    public List<SubscriptionItem> ExpiringSubscriptions(int days)
    {
        lock (_lock)
        {
            var today = NowIst.Date;
            return Users.SelectMany(u => u.Subscriptions.Where(s => s.Status == "Active").Select(s => (u, s, left: (s.End.Date - today).Days)))
                .Where(x => x.left >= 1 && x.left <= days)
                .OrderBy(x => x.s.End).ThenBy(x => x.u.Name)
                .Select(x => new SubscriptionItem(x.u.Name, x.s.Broker, x.u.Accounts.FirstOrDefault(a => a.ClientId == x.s.ClientId)?.AccountId ?? x.s.ClientId,
                    x.s.Basket, x.s.End.Date, x.left))
                .ToList();
        }
    }

    // ------------------------------------------------------------------ Basket Management → Basket Subscriptions (all users)

    public SubscriptionPage ListSubscriptions(string? status, string? search, string? basket, string? broker, int? expiringDays, int page, int pageSize)
    {
        lock (_lock)
        {
            var today = NowIst.Date;
            var all = Users.SelectMany(u => u.Subscriptions.Select(s =>
            {
                var days = (s.End.Date - today).Days;
                var st = s.Status == "Active" && days <= 0 ? "Expired" : s.Status;
                var acc = u.Accounts.FirstOrDefault(a => a.ClientId == s.ClientId);
                return new SubscriptionListItem(s.Id, u.Id, u.Name, s.Basket, Catalog.Strategies.Count(x => x.Basket == s.Basket), s.Plan, s.Months,
                    PlanPrice(s.Months), s.Multiplier, s.Start, s.End, days, st, s.Broker, acc?.AccountId ?? s.ClientId, s.ClientId);
            })).ToList();

            IEnumerable<SubscriptionListItem> q = all;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var t = search.Trim();
                q = q.Where(x => x.UserName.Contains(t, StringComparison.OrdinalIgnoreCase) || x.UserId.ToString() == t.TrimStart('#')
                              || x.AccountId.Contains(t, StringComparison.OrdinalIgnoreCase) || x.Plan.Contains(t, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(basket)) q = q.Where(x => x.Basket == basket);
            if (!string.IsNullOrWhiteSpace(broker)) q = q.Where(x => x.Broker == broker);
            if (expiringDays is { } ed) q = q.Where(x => x.Status == "Active" && x.DaysLeft <= ed);
            var scoped = q.ToList();
            var counts = new Dictionary<string, int>
            {
                ["all"] = scoped.Count,
                ["active"] = scoped.Count(x => x.Status == "Active"), ["inactive"] = scoped.Count(x => x.Status == "Inactive"),
                ["expired"] = scoped.Count(x => x.Status == "Expired"), ["paused"] = scoped.Count(x => x.Status == "Paused"),
                ["cancelled"] = scoped.Count(x => x.Status == "Cancelled"),
            };
            if (!string.IsNullOrWhiteSpace(status) && status != "all") scoped = scoped.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            var ordered = scoped.OrderByDescending(x => x.Start).ThenByDescending(x => x.Id).ToList();
            pageSize = Math.Clamp(pageSize, 1, 5000);
            var pages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)pageSize));
            page = Math.Clamp(page, 1, pages);
            return new SubscriptionPage(ordered.Count, page, pageSize, pages, counts,
                ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                all.Select(x => x.Broker).Distinct().Order().ToList());
        }
    }

    public UserWorkspace? UpdateSubscription(int id, int subId, int multiplier) => Mutate(id, u =>
    {
        var s = u.Subscriptions.FirstOrDefault(x => x.Id == subId);
        if (s is null || multiplier < 1 || multiplier > 50) return false;
        s.Multiplier = multiplier; return true;
    });

    /// <summary>Baskets → Change: the per-user multiplier for every subscription of this basket.</summary>
    public UserWorkspace? SetBasketMultiplier(int id, string basket, int multiplier) => Mutate(id, u =>
    {
        var subs = u.Subscriptions.Where(s => s.Basket == basket).ToList();
        if (subs.Count == 0 || multiplier < 1 || multiplier > 50) return false;
        subs.ForEach(s => s.Multiplier = multiplier); return true;
    });

    public UserWorkspace? AddSubscription(int id, string basket, int months, string clientId, int multiplier) => Mutate(id, u =>
    {
        var l = u.Accounts.FirstOrDefault(a => a.ClientId == clientId);
        if (l is null || !Catalog.BasketNames.Contains(basket) || months is < 1 or > 24 || multiplier is < 1 or > 50) return false;
        var start = NowIst.Date;
        u.Subscriptions.Add(new UserSubscription { Id = _nextSubId++, Basket = basket, Plan = basket.ToLowerInvariant().Replace(' ', '_'), Months = months,
            ClientId = l.ClientId, Broker = l.Broker, Multiplier = multiplier, Start = start, End = start.AddMonths(months), Status = "Active" });
        u.Payments.Add(new UserPayment { Id = _nextPayId++, SubscriptionId = _nextSubId - 1, Basket = basket, Date = NowIst, Amount = PlanPrice(months),
            Method = "UPI", Status = "Pending", Invoice = $"INV-{start:yyMM}-{_nextPayId:D5}" });
        return true;
    });
}
