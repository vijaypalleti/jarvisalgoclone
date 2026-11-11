namespace Jarvis.Api.Data;

public record BasketInfo(string Name, string? Description);

/// <param name="Underlying">NIFTY / BANKNIFTY / SENSEX for index-option strategies (used for fake signals and positions);
/// MCX / CRYPTO / null for the rest (listed in filters only).</param>
public record StrategyInfo(string Name, string Code, string Basket, string? Underlying);

/// <summary>
/// Baskets and strategies copied from the production console (names and codes only).
/// The strategy → basket and strategy → underlying links are our best guess from the names, for demo data.
/// </summary>
public static class Catalog
{
    public static readonly IReadOnlyList<BasketInfo> Baskets =
    [
        new("BANKNIFTY", null),
        new("BASKET 04", null),
        new("BASKET 05", null),
        new("BASKET 06", null),
        new("BASKET 07", null),
        new("BASKET MCX", null),
        new("DTR", null),
        new("EXPIRY", "Includes Crude, Natural Gas, and Nifty with expanded trade size."),
        new("Hedge", null),
        new("INDBULLTEST", "Bitcoin is the most popular cryptocurrency known for extreme volatility and 24x7 trading opportunities. Best suited for traders who can handle high risk and want exposure to digital assets."),
        new("Journey", null),
        new("Latency_Basket", null),
        new("MCX Full Access Basket", null),
        new("MCX MAIN", null),
        new("PYthon_Test", null),
        new("Quad instrument Basket", "Includes Crude, Natural Gas, and NIFTY with expanded trade size."),
        new("Single Instrument Basket", "Bitcoin is the most popular cryptocurrency known for extreme volatility and 24x7 trading opportunities. Best suited for traders who can handle high risk and want exposure to digital assets."),
        new("testing5paisa", null),
        new("Triple Instrument Access Basket", "Includes Includes Crude and Natural Gas access with higher lot allocation."),
    ];

    private const string N = "NIFTY", BN = "BANKNIFTY", SX = "SENSEX", MCX = "MCX", CR = "CRYPTO";

    public static readonly IReadOnlyList<StrategyInfo> Strategies =
    [
        new("b1 s1 strategy", "STRAT16280589", "PYthon_Test", null),
        new("b1 s2 strategy", "STRAT16499589", "PYthon_Test", null),
        new("b1 s3 strategy", "STRAT1710X589", "PYthon_Test", null),
        new("b2 s1 strategy", "STRAT1739T589", "PYthon_Test", null),
        new("B4_Bk_stbt", "STRAT3329K589", "BASKET 04", BN),
        new("B4_EXPIRY", "STRAT0056L589", "BASKET 04", N),
        new("B4_Journey", "STRAT0655D589", "BASKET 04", N),
        new("B4_STBT_Journey", "STRAT3551X589", "BASKET 04", N),
        new("B4_STBT_Journey_Expiry", "STRAT0408N589", "BASKET 04", N),
        new("B4_STBT_WT_updated", "STRAT3556C589", "BASKET 04", N),
        new("B4_STBT_WT_updated_Expiry", "STRAT0510O589", "BASKET 04", N),
        new("B5_BK_BUY", "STRAT3655C589", "BASKET 05", BN),
        new("B5_BK_STBT", "STRAT3730Z589", "BASKET 05", BN),
        new("B5_Next", "STRAT2610W589", "BASKET 05", N),
        new("B5_STBT_NEXT", "STRAT3802D589", "BASKET 05", N),
        new("B6_BK", "STRAT2635K589", "BASKET 06", BN),
        new("B6_BK_BUY", "STRAT5354F589", "BASKET 06", BN),
        new("B6_BK_STBT", "STRAT5422X589", "BASKET 06", BN),
        new("B6_STBT", "STRAT24547589", "BASKET 06", N),
        new("B6_stbt_tetsing", "STRAT2928W589", "BASKET 06", null),
        new("B6_STBT_WT_Test", "STRAT0235D589", "BASKET 06", null),
        new("B6_STBT_WT_updated", "STRAT5441N589", "BASKET 06", N),
        new("B6_STBT_WT|_Test", "STRAT30115589", "BASKET 06", null),
        new("B7_Bk_stbt_nifty_main", "STRAT5515V589", "BASKET 07", N),
        new("B7_Bk_stbt_sensex_main", "STRAT5538R589", "BASKET 07", SX),
        new("B7_STBT_JOURNEY", "STRAT5623H589", "BASKET 07", N),
        new("B7_STBT_JOURNEY_NIFTY", "STRAT55589589", "BASKET 07", N),
        new("BNF_Trailing_BTST", "STRAT51347589", "BANKNIFTY", BN),
        new("BTST_Buying", "STRAT2223O589", "BANKNIFTY", BN),
        new("copper", "copper", "MCX Full Access Basket", MCX),
        new("Crude MCX Test", "STRAT4804T589", "BASKET MCX", MCX),
        new("CRUDE_PR", "STRAT00292589", "BASKET MCX", MCX),
        new("crudeoil", "crudeoil", "BASKET MCX", MCX),
        new("crudeoilm", "crudeoilm", "BASKET MCX", MCX),
        new("Crypto", "STRAT4512M590", "Single Instrument Basket", CR),
        new("DTR_Strategy", "STRAT1405I589", "DTR", N),
        new("Expiry_Buying", "STRAT3436B589", "EXPIRY", N),
        new("gold", "gold", "MCX Full Access Basket", MCX),
        new("goldm", "goldm", "MCX Full Access Basket", MCX),
        new("goldpetal", "goldpetal", "MCX Full Access Basket", MCX),
        new("Hedge_Testing", "STRAT2729B589", "Hedge", N),
        new("indbull_test", "STRAT1959Z589", "INDBULLTEST", null),
        new("Journey_STBT", "STRAT59346589", "Journey", N),
        new("latency_testing", "STRAT5834N589", "Latency_Basket", null),
        new("MCX", "STRAT4511M589", "MCX MAIN", MCX),
        new("MCX S1", "STRAT47298589", "MCX MAIN", MCX),
        new("MCX S2", "STRAT5129J589", "MCX MAIN", MCX),
        new("MCX S3", "STRAT03240589", "MCX MAIN", MCX),
        new("MCX S4", "STRAT0354F589", "MCX MAIN", MCX),
        new("MCX_CRUDE", "STRAT1112F589", "BASKET MCX", MCX),
        new("mcx_crude_test_rsb", "STRAT5814G589", "BASKET MCX", MCX),
        new("mcx_test", "STRAT2236Y589", "BASKET MCX", MCX),
        new("MIDCP", "STRAT3008K589", "Quad instrument Basket", null),
        new("natgasmini", "natgasmini", "Triple Instrument Access Basket", MCX),
        new("naturalgas", "naturalgas", "Triple Instrument Access Basket", MCX),
        new("nifty", "nifty", "Quad instrument Basket", N),
        new("nifty_positional", "STRAT0606R589", "Quad instrument Basket", N),
        new("OMS_DTR_Testing", "STRAT3453A589", "DTR", N),
        new("option_test", "STRAT02218589", "PYthon_Test", null),
        new("PositionCheck", "PositionCheck78954", "PYthon_Test", null),
        new("python_DTR", "STRAT1452I589", "DTR", N),
        new("s1_v1", "STRAT3218W589", "PYthon_Test", null),
        new("S1EN", "STRAT1510A589", "DTR", N),
        new("SIEN", "STRAT3055Q589", "DTR", N),
        new("silver", "silver", "MCX Full Access Basket", MCX),
        new("silverm", "silverm", "MCX Full Access Basket", MCX),
        new("spt_1", "STRAT3307G589", "PYthon_Test", null),
        new("SSP", "STRAT3214Q589", "PYthon_Test", null),
        new("SX_STBT_WT", "STRAT2335H589", "BASKET 07", SX),
        new("SX_STBT_WT_MIS", "STRAT59556589", "BASKET 07", SX),
        new("test strategyliknk", "STRAT0359K589", "PYthon_Test", null),
        new("testing5paisa", "testing5paisa", "testing5paisa", null),
        new("testing5paisa22", "testing5paisa22", "testing5paisa", null),
        new("TestNifty", "NIFTYT", "Quad instrument Basket", N),
        new("zinc", "zinc", "MCX Full Access Basket", MCX),
    ];

    public static readonly string[] BasketNames = Baskets.Select(b => b.Name).ToArray();

    /// <summary>Index-option strategies for an underlying (fake signals/positions use these).</summary>
    public static StrategyInfo[] For(string underlying) => Strategies.Where(s => s.Underlying == underlying).ToArray();

    public static string UnderlyingOf(string symbol) =>
        symbol.StartsWith("BANKNIFTY") ? BN : symbol.StartsWith("SENSEX") ? SX : N;
}
