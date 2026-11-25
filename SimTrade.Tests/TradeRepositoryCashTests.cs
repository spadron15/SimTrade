using System.IO;
using SimTrade.Data;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.Tests;

public class TradeRepositoryCashTests : IDisposable
{
    private readonly string _path;
    private readonly TradeRepository _repository;

    public TradeRepositoryCashTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "SimTradeTests-" + Guid.NewGuid().ToString("N") + ".db");
        AppDatabase.UseDatabasePath(_path);
        var database = new AppDatabase();
        database.Initialize();
        _repository = new TradeRepository(database);
    }

    public void Dispose()
    {
        AppDatabase.UseDatabasePath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimTrade",
            "simtrade.db"));
        try
        {
            SqliteClose();
            if (File.Exists(_path))
                File.Delete(_path);
            var wal = _path + "-wal";
            var shm = _path + "-shm";
            if (File.Exists(wal))
                File.Delete(wal);
            if (File.Exists(shm))
                File.Delete(shm);
        }
        catch
        {
            // ignore cleanup races on Windows
        }

        GC.SuppressFinalize(this);
    }

    private static void SqliteClose()
    {
        // Give SQLite a beat before deleting temp files on Windows.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [Fact]
    public void DepositAndWithdraw_UpdateCashAndNetDeposits()
    {
        var created = _repository.CreateAccount("CashTest", 1_000_000m, 0m, 0m, MarketKinds.Stocks);
        Assert.True(created.Ok);

        var deposit = _repository.Deposit(created.Id, 50_000m, "aporte");
        Assert.True(deposit.Ok);
        Assert.Equal(50_000m, _repository.NetDeposits(created.Id));

        var account = _repository.GetAccount(created.Id);
        Assert.NotNull(account);
        Assert.Equal(1_050_000m, account!.Cash);

        var withdraw = _repository.Withdraw(created.Id, 20_000m, "retiro");
        Assert.True(withdraw.Ok);
        Assert.Equal(30_000m, _repository.NetDeposits(created.Id));
        account = _repository.GetAccount(created.Id);
        Assert.Equal(1_030_000m, account!.Cash);
    }

    [Fact]
    public void Sell_PersistsRealizedPnl()
    {
        var created = _repository.CreateAccount("PnlTest", 1_000_000m, 0m, 0m, MarketKinds.Crypto);
        Assert.True(created.Ok);

        var buy = _repository.Buy(created.Id, "BTC-USD", "Bitcoin", "USD", 0.01m, 100_000m, 5000m, 20m);
        Assert.True(buy.Ok);

        var sell = _repository.Sell(created.Id, "BTC-USD", "Bitcoin", "USD", 0.01m, 110_000m, 5500m, 20m);
        Assert.True(sell.Ok);

        var trades = _repository.ListTrades(created.Id);
        var sale = trades.First(trade => trade.Side == "SELL");
        Assert.NotNull(sale.RealizedPnlMxn);
        Assert.Equal(100m, sale.RealizedPnlMxn);
        Assert.Equal(100m, _repository.SumRealizedPnl(created.Id));
    }

    [Fact]
    public void CryptoBuy_AllowsEightDecimalQuantity()
    {
        var qty = PortfolioMath.RoundQuantity(0.00000001m, crypto: true);
        Assert.Equal(0.00000001m, qty);

        var created = _repository.CreateAccount("CryptoQty", 1_000_000m, 0m, 0m, MarketKinds.Crypto);
        Assert.True(created.Ok);
        var buy = _repository.Buy(created.Id, "ETH-USD", "Ethereum", "USD", qty, 50_000m, 2500m, 20m);
        Assert.True(buy.Ok);
        var position = _repository.ListPositions(created.Id).Single();
        Assert.Equal(qty, position.Quantity);
    }

    [Fact]
    public void EnqueueLimit_SetsExpiresAtWithinValidity()
    {
        var created = _repository.CreateAccount("LimitExpiry", 1_000_000m, 0m, 0m, MarketKinds.Crypto);
        Assert.True(created.Ok);

        var enqueue = _repository.Enqueue(
            created.Id, "BTC-USD", "Bitcoin", "USD", buy: true,
            quantity: 0.01m, priceMxn: 100_000m, quotePrice: 5000m, fxRate: 20m,
            orderType: OrderTypes.Limit, limitPriceMxn: 90_000m, validityDays: 7);
        Assert.True(enqueue.Ok);

        var pending = Assert.Single(_repository.ListPending(created.Id));
        Assert.NotNull(pending.ExpiresAt);
        var localExpiry = pending.ExpiresAt!.Value.ToLocalTime().Date;
        Assert.Equal(DateTime.Today.AddDays(7), localExpiry);
        Assert.Equal(0, _repository.CancelExpiredPendingOrders());
        Assert.Single(_repository.ListPending(created.Id));
    }
}
