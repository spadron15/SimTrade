using System.Globalization;
using Microsoft.Data.Sqlite;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.Data;

public sealed class TradeRepository
{
    private readonly AppDatabase _database;
    private readonly object _gate = new();

    public TradeRepository(AppDatabase database) => _database = database;

    public IReadOnlyList<AccountData> ListAccounts()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Name, InitialCash, Cash, BuyCommissionPercent, SellCommissionPercent, MarketKind, CreatedAt, Icon, Notes
                FROM Account
                ORDER BY Name;
                """;
            using var reader = command.ExecuteReader();
            var list = new List<AccountData>();
            while (reader.Read())
                list.Add(ReadAccount(reader));
            return list;
        }
    }

    public AccountData? GetAccount(long id)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            return ReadAccount(connection, id, null);
        }
    }

    public OperationResult CreateAccount(
        string name,
        decimal initialCash,
        decimal buyPercent,
        decimal sellPercent,
        string marketKind,
        string? icon = null,
        string? notes = null)
    {
        var validation = ValidateAccount(name, initialCash, buyPercent, sellPercent);
        if (!validation.Ok)
            return validation;

        var notesValue = NormalizeNotes(notes);
        if (notesValue.Length > 4000)
            return new OperationResult(false, "La descripción puede tener hasta 4000 caracteres.");

        var kind = MarketKinds.Normalize(marketKind);
        var iconValue = AccountIconCache.Normalize(icon);
        lock (_gate)
        {
            using var connection = _database.Open();
            if (NameTaken(connection, name, null))
                return new OperationResult(false, "Ya existe una cuenta con ese nombre.");

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Account (Name, InitialCash, Cash, BuyCommissionPercent, SellCommissionPercent, MarketKind, Icon, Notes, CreatedAt)
                VALUES ($name, $initial, $cash, $buy, $sell, $kind, $icon, $notes, $created);
                """;
            Add(command, "$name", name.Trim());
            Add(command, "$initial", Store(initialCash));
            Add(command, "$cash", Store(initialCash));
            Add(command, "$buy", Store(buyPercent));
            Add(command, "$sell", Store(sellPercent));
            Add(command, "$kind", kind);
            Add(command, "$icon", iconValue);
            Add(command, "$notes", notesValue);
            Add(command, "$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();

            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT last_insert_rowid();";
            var id = (long)(identity.ExecuteScalar() ?? 0L);
            return new OperationResult(true, "Cuenta creada.", id);
        }
    }

    public OperationResult UpdateAccount(
        long id,
        string name,
        decimal buyPercent,
        decimal sellPercent,
        string marketKind,
        string? icon = null,
        string? notes = null)
    {
        var validation = ValidateAccount(name, 1, buyPercent, sellPercent);
        if (!validation.Ok)
            return validation;

        var notesValue = NormalizeNotes(notes);
        if (notesValue.Length > 4000)
            return new OperationResult(false, "La descripción puede tener hasta 4000 caracteres.");

        var kind = MarketKinds.Normalize(marketKind);
        var iconValue = AccountIconCache.Normalize(icon);
        lock (_gate)
        {
            using var connection = _database.Open();
            var current = ReadAccount(connection, id, null);
            if (current is null)
                return new OperationResult(false, "No se encontró la cuenta.");
            if (NameTaken(connection, name, id))
                return new OperationResult(false, "Ya existe una cuenta con ese nombre.");

            if (!string.Equals(current.MarketKind, kind, StringComparison.OrdinalIgnoreCase)
                && CountPositions(connection, id) > 0)
            {
                return new OperationResult(false, "No se puede cambiar Bolsa/Crypto mientras la cuenta tenga posiciones.");
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Account
                SET Name = $name,
                    BuyCommissionPercent = $buy,
                    SellCommissionPercent = $sell,
                    MarketKind = $kind,
                    Icon = $icon,
                    Notes = $notes
                WHERE Id = $id;
                """;
            Add(command, "$name", name.Trim());
            Add(command, "$buy", Store(buyPercent));
            Add(command, "$sell", Store(sellPercent));
            Add(command, "$kind", kind);
            Add(command, "$icon", iconValue);
            Add(command, "$notes", notesValue);
            Add(command, "$id", id);
            command.ExecuteNonQuery();
            return new OperationResult(true, "Cuenta actualizada.", id);
        }
    }

    private static string NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? "" : notes.Trim();

    public int CountPositions(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            return CountPositions(connection, accountId);
        }
    }

    private static int CountPositions(SqliteConnection connection, long accountId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Position WHERE AccountId = $id;";
        Add(command, "$id", accountId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public OperationResult DeleteAccount(long id)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Account WHERE Id = $id;";
            Add(command, "$id", id);
            var deleted = command.ExecuteNonQuery();
            return deleted == 0
                ? new OperationResult(false, "No se encontró la cuenta.")
                : new OperationResult(true, "Cuenta eliminada.", id);
        }
    }

    public IReadOnlyList<PositionData> ListPositions(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Symbol, CompanyName, Currency, Quantity, AverageCost, TotalCost, OpenedAt
                FROM Position
                WHERE AccountId = $account
                ORDER BY Symbol;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            var list = new List<PositionData>();
            while (reader.Read())
            {
                list.Add(new PositionData
                {
                    Id = reader.GetInt64(0),
                    Symbol = reader.GetString(1),
                    CompanyName = reader.GetString(2),
                    Currency = reader.GetString(3),
                    Quantity = Load(reader, 4),
                    AverageCost = Load(reader, 5),
                    TotalCost = Load(reader, 6),
                    OpenedAt = ReadOpened(reader, 7)
                });
            }
            return list;
        }
    }

    public IReadOnlyList<TradeData> ListTrades(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Symbol, CompanyName, Side, Quantity, PriceMxn, QuotePrice, QuoteCurrency,
                       FxRate, GrossMxn, CommissionMxn, CashAfter, AverageCostAfter, QuantityAfter, CreatedAt,
                       RealizedPnlMxn, Notes
                FROM Trade
                WHERE AccountId = $account
                ORDER BY Id DESC;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            var list = new List<TradeData>();
            while (reader.Read())
            {
                list.Add(new TradeData
                {
                    Id = reader.GetInt64(0),
                    Symbol = reader.GetString(1),
                    CompanyName = reader.GetString(2),
                    Side = reader.GetString(3),
                    Quantity = Load(reader, 4),
                    PriceMxn = Load(reader, 5),
                    QuotePrice = Load(reader, 6),
                    QuoteCurrency = reader.GetString(7),
                    FxRate = reader.IsDBNull(8) ? null : Load(reader, 8),
                    GrossMxn = Load(reader, 9),
                    CommissionMxn = Load(reader, 10),
                    CashAfter = Load(reader, 11),
                    AverageCostAfter = Load(reader, 12),
                    QuantityAfter = Load(reader, 13),
                    CreatedAt = ParseDate(reader.GetString(14)),
                    RealizedPnlMxn = reader.FieldCount > 15 && !reader.IsDBNull(15) ? Load(reader, 15) : null,
                    Notes = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : ""
                });
            }
            return list;
        }
    }

    public OperationResult UpdateTradeNotes(long tradeId, string notes)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Trade SET Notes = $notes WHERE Id = $id;";
            Add(command, "$notes", notes?.Trim() ?? "");
            Add(command, "$id", tradeId);
            var changed = command.ExecuteNonQuery();
            return changed > 0
                ? new OperationResult(true, "Nota guardada.")
                : new OperationResult(false, "No se encontró la operación.");
        }
    }

    public decimal NetDeposits(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            return NetDeposits(connection, null, accountId);
        }
    }

    public decimal SumRealizedPnl(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT RealizedPnlMxn FROM Trade
                WHERE AccountId = $account AND Side = 'SELL' AND RealizedPnlMxn IS NOT NULL;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            decimal total = 0;
            while (reader.Read())
                total += Load(reader, 0);
            return total;
        }
    }

    public OperationResult Deposit(long accountId, decimal amount, string? note = null)
    {
        amount = PortfolioMath.RoundMoney(amount);
        if (amount <= 0)
            return new OperationResult(false, "La aportación debe ser mayor que cero.");

        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            var account = ReadAccount(connection, accountId, tx);
            if (account is null)
                return new OperationResult(false, "No se encontró la cuenta.");

            var cashAfter = account.Cash + amount;
            UpdateCash(connection, tx, accountId, cashAfter);
            InsertCashMovement(connection, tx, accountId, CashMovementKinds.Deposit, amount, cashAfter, note);
            tx.Commit();
            return new OperationResult(true,
                "Aportación registrada: " + MxFormat.Money(amount)
                + ". Efectivo ahora " + MxFormat.Money(cashAfter) + ".");
        }
    }

    public OperationResult Withdraw(long accountId, decimal amount, string? note = null)
    {
        amount = PortfolioMath.RoundMoney(amount);
        if (amount <= 0)
            return new OperationResult(false, "El retiro debe ser mayor que cero.");

        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            var account = ReadAccount(connection, accountId, tx);
            if (account is null)
                return new OperationResult(false, "No se encontró la cuenta.");

            if (amount > account.Cash)
            {
                var held = SumReservedBuys(connection, tx, accountId, null);
                return new OperationResult(false, held > 0
                    ? "No hay efectivo disponible suficiente (" + MxFormat.Money(account.Cash)
                      + "). Hay " + MxFormat.Money(held) + " apartados en órdenes de compra en cola."
                    : "El efectivo disponible (" + MxFormat.Money(account.Cash) + ") no alcanza para este retiro.");
            }

            var cashAfter = account.Cash - amount;
            UpdateCash(connection, tx, accountId, cashAfter);
            InsertCashMovement(connection, tx, accountId, CashMovementKinds.Withdraw, amount, cashAfter, note);
            tx.Commit();
            return new OperationResult(true,
                "Retiro registrado: " + MxFormat.Money(amount)
                + ". Efectivo ahora " + MxFormat.Money(cashAfter) + ".");
        }
    }

    public IReadOnlyList<CashMovementData> ListCashMovements(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, AccountId, Kind, AmountMxn, CashAfter, Note, CreatedAt
                FROM CashMovement
                WHERE AccountId = $account
                ORDER BY Id DESC;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            var list = new List<CashMovementData>();
            while (reader.Read())
            {
                list.Add(new CashMovementData
                {
                    Id = reader.GetInt64(0),
                    AccountId = reader.GetInt64(1),
                    Kind = reader.GetString(2),
                    AmountMxn = Load(reader, 3),
                    CashAfter = Load(reader, 4),
                    Note = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    CreatedAt = ParseDate(reader.GetString(6))
                });
            }
            return list;
        }
    }

    public IReadOnlyList<PendingOrderData> ListPending(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, AccountId, Symbol, CompanyName, Currency, Side, OrderType, Quantity,
                       ReservedMxn, QuotePrice, LimitPriceMxn, FxRate, CreatedAt, ExpiresAt
                FROM PendingOrder
                WHERE AccountId = $account
                ORDER BY Id;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            var list = new List<PendingOrderData>();
            while (reader.Read())
                list.Add(ReadPending(reader));
            return list;
        }
    }

    public IReadOnlyList<PendingOrderData> ListAllPending()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, AccountId, Symbol, CompanyName, Currency, Side, OrderType, Quantity,
                       ReservedMxn, QuotePrice, LimitPriceMxn, FxRate, CreatedAt, ExpiresAt
                FROM PendingOrder
                ORDER BY Id;
                """;
            using var reader = command.ExecuteReader();
            var list = new List<PendingOrderData>();
            while (reader.Read())
                list.Add(ReadPending(reader));
            return list;
        }
    }

    public OperationResult Enqueue(
        long accountId,
        string symbol,
        string companyName,
        string currency,
        bool buy,
        decimal quantity,
        decimal priceMxn,
        decimal quotePrice,
        decimal? fxRate,
        string orderType = OrderTypes.Market,
        decimal? limitPriceMxn = null,
        int validityDays = 5)
    {
        var type = OrderTypes.Normalize(orderType);
        var needsTrigger = OrderTypes.NeedsTriggerPrice(type);
        var reservePrice = needsTrigger
            ? decimal.Round(limitPriceMxn ?? 0, 4, MidpointRounding.AwayFromZero)
            : priceMxn;
        if (needsTrigger && reservePrice <= 0)
            return new OperationResult(false, "Escribe un precio de activación mayor que cero.");

        var days = Math.Clamp(validityDays, 1, 30);
        DateTime? expiresAt = needsTrigger
            ? DateTime.SpecifyKind(DateTime.Today.AddDays(days), DateTimeKind.Local).ToUniversalTime()
            : null;

        var input = ValidateTrade(symbol, companyName, currency, quantity, reservePrice);
        if (!input.Ok)
            return input;

        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            var account = ReadAccount(connection, accountId, tx);
            if (account is null)
                return new OperationResult(false, "No se encontró la cuenta.");

            var position = ReadPosition(connection, accountId, symbol, tx);
            decimal reserved = 0;
            if (buy)
            {
                var math = PortfolioMath.ComputeBuy(position?.Quantity ?? 0, position?.TotalCost ?? 0, quantity, reservePrice, account.BuyCommissionPercent);
                reserved = math.Debit;
                // El efectivo de la cuenta ya excluye apartados previos (hard hold).
                if (math.Debit > account.Cash)
                {
                    return new OperationResult(false,
                        "No hay efectivo disponible suficiente para esta compra. Disponible: "
                        + MxFormat.Money(account.Cash) + " · se necesitan " + MxFormat.Money(math.Debit) + ".");
                }

                UpdateCash(connection, tx, accountId, account.Cash - reserved);
            }
            else
            {
                var heldShares = SumPendingSells(connection, tx, accountId, symbol, null);
                var free = (position?.Quantity ?? 0) - heldShares;
                if (position is null || quantity > free)
                {
                    return new OperationResult(false, heldShares > 0
                        ? "Esas acciones ya están apartadas en una orden en cola."
                        : "No hay suficientes acciones para esta venta.");
                }
            }

            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO PendingOrder (
                    AccountId, Symbol, CompanyName, Currency, Side, OrderType, Quantity,
                    ReservedMxn, QuotePrice, LimitPriceMxn, FxRate, CreatedAt, ExpiresAt)
                VALUES (
                    $account, $symbol, $company, $currency, $side, $type, $quantity,
                    $reserved, $quote, $limit, $fx, $created, $expires);
                """;
            Add(command, "$account", accountId);
            Add(command, "$symbol", symbol);
            Add(command, "$company", companyName);
            Add(command, "$currency", currency);
            Add(command, "$side", buy ? "BUY" : "SELL");
            Add(command, "$type", type);
            Add(command, "$quantity", Store(quantity));
            Add(command, "$reserved", Store(reserved));
            Add(command, "$quote", Store(quotePrice));
            Add(command, "$limit", needsTrigger ? Store(reservePrice) : null);
            Add(command, "$fx", fxRate is null ? null : Store(fxRate.Value));
            Add(command, "$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Add(command, "$expires", expiresAt?.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            tx.Commit();

            if (needsTrigger)
            {
                var label = OrderTypes.Label(type).ToLowerInvariant();
                var until = expiresAt!.Value.ToLocalTime().ToString("dd MMM yyyy", MxFormat.Culture);
                return new OperationResult(true, buy
                    ? $"Orden {label} de compra en cola a {MxFormat.Price(reservePrice)} · vigencia {days} día(s) (hasta {until}). Se descontaron {MxFormat.Money(reserved)} del efectivo disponible."
                    : $"Orden {label} de venta en cola a {MxFormat.Price(reservePrice)} · vigencia {days} día(s) (hasta {until}). Esas acciones quedan apartadas.");
            }

            return new OperationResult(true, buy
                ? $"La compra quedó en cola. Se descontaron {MxFormat.Money(reserved)} del efectivo disponible hasta la apertura."
                : "La venta quedó en cola. Esas acciones quedan apartadas hasta la apertura.");
        }
    }

    /// <summary>
    /// Cancela órdenes con precio de activación cuya vigencia ya venció y libera apartados.
    /// </summary>
    public int CancelExpiredPendingOrders()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            using var select = connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = """
                SELECT Id, AccountId, Side, ReservedMxn, ExpiresAt
                FROM PendingOrder
                WHERE ExpiresAt IS NOT NULL;
                """;
            var now = DateTime.UtcNow;
            var refunds = new List<(long AccountId, decimal Amount)>();
            var ids = new List<(long AccountId, long Id)>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader.IsDBNull(4))
                        continue;
                    var expires = ParseDate(reader.GetString(4));
                    if (expires.ToUniversalTime() > now)
                        continue;

                    var id = reader.GetInt64(0);
                    var accountId = reader.GetInt64(1);
                    var side = reader.GetString(2);
                    var reserved = Load(reader, 3);
                    ids.Add((accountId, id));
                    if (side.Equals("BUY", StringComparison.OrdinalIgnoreCase) && reserved > 0)
                        refunds.Add((accountId, reserved));
                }
            }

            foreach (var (accountId, amount) in refunds)
            {
                var account = ReadAccount(connection, accountId, tx);
                if (account is null)
                    continue;
                UpdateCash(connection, tx, accountId, account.Cash + amount);
            }

            var deleted = 0;
            foreach (var (accountId, id) in ids)
                deleted += DeletePending(connection, tx, accountId, id);

            tx.Commit();
            return deleted;
        }
    }

    public int CancelLimitOrdersForStocksAccounts()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            using var select = connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = """
                SELECT Id, AccountId, Side, ReservedMxn
                FROM PendingOrder
                WHERE OrderType = 'Limit'
                  AND AccountId IN (
                      SELECT Id FROM Account
                      WHERE LOWER(COALESCE(MarketKind, 'Stocks')) != 'crypto'
                  );
                """;
            var refunds = new List<(long AccountId, decimal Amount)>();
            var ids = new List<(long AccountId, long Id)>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var accountId = reader.GetInt64(1);
                    var side = reader.GetString(2);
                    var reserved = Load(reader, 3);
                    ids.Add((accountId, id));
                    if (side.Equals("BUY", StringComparison.OrdinalIgnoreCase) && reserved > 0)
                        refunds.Add((accountId, reserved));
                }
            }

            foreach (var (accountId, amount) in refunds)
            {
                var account = ReadAccount(connection, accountId, tx);
                if (account is null)
                    continue;
                UpdateCash(connection, tx, accountId, account.Cash + amount);
            }

            var deleted = 0;
            foreach (var (accountId, id) in ids)
                deleted += DeletePending(connection, tx, accountId, id);

            tx.Commit();
            return deleted;
        }
    }

    public OperationResult CancelPending(long accountId, long orderId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            var order = ReadPendingOrder(connection, tx, accountId, orderId);
            if (order is null)
                return new OperationResult(false, "Esa orden ya no está en la cola.");

            if (order.IsBuy && order.ReservedMxn > 0)
            {
                var account = ReadAccount(connection, accountId, tx);
                if (account is null)
                    return new OperationResult(false, "No se encontró la cuenta.");
                UpdateCash(connection, tx, accountId, account.Cash + order.ReservedMxn);
            }

            if (DeletePending(connection, tx, accountId, orderId) != 1)
                return new OperationResult(false, "Esa orden ya no está en la cola.");

            tx.Commit();
            return new OperationResult(true, order.IsBuy && order.ReservedMxn > 0
                ? "Se canceló la orden y se devolvieron " + MxFormat.Money(order.ReservedMxn) + " al efectivo disponible."
                : "Se canceló la orden y se liberó lo apartado.");
        }
    }

    /// <summary>
    /// Una sola vez: convierte apartados "suaves" antiguos en descuento real de Cash.
    /// </summary>
    public void EnsureHardCashHoldsMigrated()
    {
        if (string.Equals(GetSetting("PendingHardCashHold"), "1", StringComparison.Ordinal))
            return;

        lock (_gate)
        {
            if (string.Equals(GetSetting("PendingHardCashHold"), "1", StringComparison.Ordinal))
                return;

            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            using var select = connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = """
                SELECT AccountId, ReservedMxn
                FROM PendingOrder
                WHERE Side = 'BUY' AND CAST(ReservedMxn AS REAL) > 0;
                """;
            var byAccount = new Dictionary<long, decimal>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var accountId = reader.GetInt64(0);
                    var amount = Load(reader, 1);
                    byAccount[accountId] = byAccount.GetValueOrDefault(accountId) + amount;
                }
            }

            foreach (var (accountId, amount) in byAccount)
            {
                var account = ReadAccount(connection, accountId, tx);
                if (account is null)
                    continue;
                var next = account.Cash - amount;
                if (next < 0)
                    next = 0;
                UpdateCash(connection, tx, accountId, next);
            }

            tx.Commit();
        }

        SetSetting("PendingHardCashHold", "1");
    }

    public OperationResult Buy(
        long accountId,
        string symbol,
        string companyName,
        string currency,
        decimal quantity,
        decimal priceMxn,
        decimal quotePrice,
        decimal? fxRate,
        long? fulfillingOrderId = null)
    {
        var input = ValidateTrade(symbol, companyName, currency, quantity, priceMxn);
        if (!input.Ok)
            return input;

        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            if (fulfillingOrderId is long pendingId && !PendingExists(connection, tx, accountId, pendingId))
                return new OperationResult(false, "Esa orden ya no está en la cola.");

            var account = ReadAccount(connection, accountId, tx);
            if (account is null)
                return new OperationResult(false, "No se encontró la cuenta.");

            var position = ReadPosition(connection, accountId, symbol, tx);
            var oldQuantity = position?.Quantity ?? 0;
            var oldTotal = position?.TotalCost ?? 0;
            var math = PortfolioMath.ComputeBuy(oldQuantity, oldTotal, quantity, priceMxn, account.BuyCommissionPercent);
            decimal reservedForOrder = 0;
            if (fulfillingOrderId is long pendingToFill)
            {
                var pending = ReadPendingOrder(connection, tx, accountId, pendingToFill);
                if (pending is null)
                    return new OperationResult(false, "Esa orden ya no está en la cola.");
                reservedForOrder = pending.ReservedMxn;
            }

            // Con hard hold, Cash ya excluye ReservedMxn de la orden en cola.
            // Solo se ajusta la diferencia entre el costo real y lo apartado.
            var extra = math.Debit - reservedForOrder;
            if (extra > account.Cash)
            {
                var message = fulfillingOrderId is null
                    ? "No hay efectivo disponible suficiente para esta compra, incluida la comisión. Disponible: "
                      + MxFormat.Money(account.Cash) + "."
                    : "Al ejecutar, el precio superó lo apartado y no hay efectivo disponible suficiente. La orden sigue en cola.";
                return new OperationResult(false, message);
            }

            var cashAfter = account.Cash - extra;
            UpdateCash(connection, tx, accountId, cashAfter);
            UpsertPosition(connection, tx, accountId, position?.Id, symbol, companyName, currency, math.NewQuantity, math.NewAverage, math.NewTotalCost, position?.OpenedAt ?? DateTime.UtcNow);
            InsertTrade(connection, tx, accountId, symbol, companyName, "BUY", quantity, priceMxn, quotePrice, currency, fxRate, math.Gross, math.Commission, cashAfter, math.NewAverage, math.NewQuantity, null);
            if (fulfillingOrderId is long filledId && DeletePending(connection, tx, accountId, filledId) != 1)
                return new OperationResult(false, "Esa orden ya no está en la cola.");
            tx.Commit();
            return new OperationResult(true, "Compra registrada. El costo promedio de la posición quedó actualizado.");
        }
    }

    public OperationResult Sell(
        long accountId,
        string symbol,
        string companyName,
        string currency,
        decimal quantity,
        decimal priceMxn,
        decimal quotePrice,
        decimal? fxRate,
        long? fulfillingOrderId = null)
    {
        var input = ValidateTrade(symbol, companyName, currency, quantity, priceMxn);
        if (!input.Ok)
            return input;

        lock (_gate)
        {
            using var connection = _database.Open();
            using var tx = connection.BeginTransaction();
            if (fulfillingOrderId is long pendingId && !PendingExists(connection, tx, accountId, pendingId))
                return new OperationResult(false, "Esa orden ya no está en la cola.");

            var account = ReadAccount(connection, accountId, tx);
            if (account is null)
                return new OperationResult(false, "No se encontró la cuenta.");

            var position = ReadPosition(connection, accountId, symbol, tx);
            var heldShares = SumPendingSells(connection, tx, accountId, symbol, fulfillingOrderId);
            var free = (position?.Quantity ?? 0) - heldShares;
            if (position is null || quantity > free)
            {
                var message = fulfillingOrderId is null
                    ? heldShares > 0
                        ? "No hay suficientes acciones libres. Parte está apartada en la cola."
                        : "No hay suficientes acciones para esta venta."
                    : "Al abrir ya no había acciones suficientes. La orden sigue en cola.";
                return new OperationResult(false, message);
            }

            var math = PortfolioMath.ComputeSell(position.Quantity, position.TotalCost, quantity, priceMxn, account.SellCommissionPercent);
            var cashAfter = account.Cash + math.Credit;
            if (cashAfter < 0)
                return new OperationResult(false, "La comisión de la venta dejaría el efectivo en negativo.");

            UpdateCash(connection, tx, accountId, cashAfter);
            if (math.NewQuantity == 0)
                DeletePosition(connection, tx, position.Id);
            else
                UpsertPosition(connection, tx, accountId, position.Id, symbol, companyName, currency, math.NewQuantity, math.NewAverage, math.NewTotalCost, position.OpenedAt);

            InsertTrade(connection, tx, accountId, symbol, companyName, "SELL", quantity, priceMxn, quotePrice, currency, fxRate, math.Gross, math.Commission, cashAfter, math.NewAverage, math.NewQuantity, math.RealizedPnl);
            if (fulfillingOrderId is long filledId && DeletePending(connection, tx, accountId, filledId) != 1)
                return new OperationResult(false, "Esa orden ya no está en la cola.");
            tx.Commit();
            var pnl = " Resultado realizado: " + MxFormat.SignedMoney(math.RealizedPnl) + ".";
            return new OperationResult(true, math.NewQuantity == 0
                ? "Venta registrada. La posición quedó cerrada." + pnl
                : "Venta registrada." + pnl);
        }
    }

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Setting WHERE Key = $key;";
            Add(command, "$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Setting (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            Add(command, "$key", key);
            Add(command, "$value", value);
            command.ExecuteNonQuery();
        }
    }

    private static OperationResult ValidateAccount(string name, decimal initialCash, decimal buyPercent, decimal sellPercent)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new OperationResult(false, "Escribe un nombre para la cuenta.");
        if (name.Trim().Length > 60)
            return new OperationResult(false, "El nombre de la cuenta puede tener hasta 60 caracteres.");
        if (initialCash <= 0)
            return new OperationResult(false, "El efectivo inicial debe ser mayor que cero.");
        if (initialCash > 1_000_000_000_000m)
            return new OperationResult(false, "El efectivo inicial es demasiado grande.");
        if (buyPercent < 0 || sellPercent < 0 || buyPercent > 100 || sellPercent > 100)
            return new OperationResult(false, "La comisión debe estar entre 0% y 100%.");
        return new OperationResult(true, "");
    }

    private static OperationResult ValidateTrade(string symbol, string companyName, string currency, decimal quantity, decimal priceMxn)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return new OperationResult(false, "Falta el símbolo.");
        if (quantity <= 0)
            return new OperationResult(false, "La cantidad debe ser mayor que cero.");
        if (priceMxn <= 0)
            return new OperationResult(false, "El precio debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(currency))
            return new OperationResult(false, "Faltan los datos del instrumento.");
        return new OperationResult(true, "");
    }

    private static bool NameTaken(SqliteConnection connection, string name, long? exceptId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = exceptId is null
            ? "SELECT 1 FROM Account WHERE Name = $name COLLATE NOCASE LIMIT 1;"
            : "SELECT 1 FROM Account WHERE Name = $name COLLATE NOCASE AND Id <> $id LIMIT 1;";
        Add(command, "$name", name.Trim());
        if (exceptId is long id)
            Add(command, "$id", id);
        return command.ExecuteScalar() is not null;
    }

    private static AccountData? ReadAccount(SqliteConnection connection, long id, SqliteTransaction? tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT Id, Name, InitialCash, Cash, BuyCommissionPercent, SellCommissionPercent, MarketKind, CreatedAt, Icon, Notes
            FROM Account
            WHERE Id = $id;
            """;
        Add(command, "$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    private static AccountData ReadAccount(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Name = reader.GetString(1),
        InitialCash = Load(reader, 2),
        Cash = Load(reader, 3),
        BuyCommissionPercent = Load(reader, 4),
        SellCommissionPercent = Load(reader, 5),
        MarketKind = MarketKinds.Normalize(reader.IsDBNull(6) ? null : reader.GetString(6)),
        CreatedAt = ParseDate(reader.GetString(7)),
        Icon = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : "",
        Notes = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : ""
    };

    private static PendingOrderData ReadPending(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        AccountId = reader.GetInt64(1),
        Symbol = reader.GetString(2),
        CompanyName = reader.GetString(3),
        Currency = reader.GetString(4),
        Side = reader.GetString(5),
        OrderType = OrderTypes.Normalize(reader.IsDBNull(6) ? null : reader.GetString(6)),
        Quantity = Load(reader, 7),
        ReservedMxn = Load(reader, 8),
        QuotePrice = Load(reader, 9),
        LimitPriceMxn = reader.IsDBNull(10) ? null : Load(reader, 10),
        FxRate = reader.IsDBNull(11) ? null : Load(reader, 11),
        CreatedAt = ParseDate(reader.GetString(12)),
        ExpiresAt = reader.FieldCount > 13 && !reader.IsDBNull(13) ? ParseDate(reader.GetString(13)) : null
    };

    private static PositionData? ReadPosition(SqliteConnection connection, long accountId, string symbol, SqliteTransaction tx)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT Id, Symbol, CompanyName, Currency, Quantity, AverageCost, TotalCost, OpenedAt
            FROM Position
            WHERE AccountId = $account AND Symbol = $symbol COLLATE NOCASE;
            """;
        Add(command, "$account", accountId);
        Add(command, "$symbol", symbol);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new PositionData
        {
            Id = reader.GetInt64(0),
            Symbol = reader.GetString(1),
            CompanyName = reader.GetString(2),
            Currency = reader.GetString(3),
            Quantity = Load(reader, 4),
            AverageCost = Load(reader, 5),
            TotalCost = Load(reader, 6),
            OpenedAt = ReadOpened(reader, 7)
        };
    }

    private static DateTime? ReadOpened(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static bool PendingExists(SqliteConnection connection, SqliteTransaction tx, long accountId, long orderId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT 1 FROM PendingOrder WHERE Id = $id AND AccountId = $account;";
        Add(command, "$id", orderId);
        Add(command, "$account", accountId);
        return command.ExecuteScalar() is not null;
    }

    private static PendingOrderData? ReadPendingOrder(
        SqliteConnection connection,
        SqliteTransaction tx,
        long accountId,
        long orderId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT Id, AccountId, Symbol, CompanyName, Currency, Side, OrderType, Quantity,
                   ReservedMxn, QuotePrice, LimitPriceMxn, FxRate, CreatedAt, ExpiresAt
            FROM PendingOrder
            WHERE Id = $id AND AccountId = $account;
            """;
        Add(command, "$id", orderId);
        Add(command, "$account", accountId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPending(reader) : null;
    }

    private static decimal SumReservedBuys(SqliteConnection connection, SqliteTransaction tx, long accountId, long? exceptId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = exceptId is null
            ? "SELECT ReservedMxn FROM PendingOrder WHERE AccountId = $account AND Side = 'BUY';"
            : "SELECT ReservedMxn FROM PendingOrder WHERE AccountId = $account AND Side = 'BUY' AND Id <> $except;";
        Add(command, "$account", accountId);
        if (exceptId is long id)
            Add(command, "$except", id);
        using var reader = command.ExecuteReader();
        decimal sum = 0;
        while (reader.Read())
            sum += Load(reader, 0);
        return sum;
    }

    private static decimal SumPendingSells(SqliteConnection connection, SqliteTransaction tx, long accountId, string symbol, long? exceptId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = exceptId is null
            ? "SELECT Quantity FROM PendingOrder WHERE AccountId = $account AND Side = 'SELL' AND Symbol = $symbol COLLATE NOCASE;"
            : "SELECT Quantity FROM PendingOrder WHERE AccountId = $account AND Side = 'SELL' AND Symbol = $symbol COLLATE NOCASE AND Id <> $except;";
        Add(command, "$account", accountId);
        Add(command, "$symbol", symbol);
        if (exceptId is long id)
            Add(command, "$except", id);
        using var reader = command.ExecuteReader();
        decimal sum = 0;
        while (reader.Read())
            sum += Load(reader, 0);
        return sum;
    }

    private static int DeletePending(SqliteConnection connection, SqliteTransaction tx, long accountId, long orderId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "DELETE FROM PendingOrder WHERE Id = $id AND AccountId = $account;";
        Add(command, "$id", orderId);
        Add(command, "$account", accountId);
        return command.ExecuteNonQuery();
    }

    private static void UpdateCash(SqliteConnection connection, SqliteTransaction tx, long accountId, decimal cash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "UPDATE Account SET Cash = $cash WHERE Id = $id;";
        Add(command, "$cash", Store(cash));
        Add(command, "$id", accountId);
        command.ExecuteNonQuery();
    }

    private static void UpsertPosition(
        SqliteConnection connection,
        SqliteTransaction tx,
        long accountId,
        long? positionId,
        string symbol,
        string companyName,
        string currency,
        decimal quantity,
        decimal average,
        decimal totalCost,
        DateTime? openedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        if (positionId is null)
        {
            command.CommandText = """
                INSERT INTO Position (AccountId, Symbol, CompanyName, Currency, Quantity, AverageCost, TotalCost, OpenedAt)
                VALUES ($account, $symbol, $company, $currency, $quantity, $average, $total, $opened);
                """;
            Add(command, "$account", accountId);
            Add(command, "$opened", (openedAt ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture));
        }
        else
        {
            command.CommandText = """
                UPDATE Position
                SET Symbol = $symbol,
                    CompanyName = $company,
                    Currency = $currency,
                    Quantity = $quantity,
                    AverageCost = $average,
                    TotalCost = $total,
                    OpenedAt = COALESCE(OpenedAt, $opened)
                WHERE Id = $id;
                """;
            Add(command, "$id", positionId.Value);
            Add(command, "$opened", (openedAt ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture));
        }

        Add(command, "$symbol", symbol);
        Add(command, "$company", companyName);
        Add(command, "$currency", currency);
        Add(command, "$quantity", Store(quantity));
        Add(command, "$average", Store(average));
        Add(command, "$total", Store(totalCost));
        command.ExecuteNonQuery();
    }

    private static void DeletePosition(SqliteConnection connection, SqliteTransaction tx, long positionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "DELETE FROM Position WHERE Id = $id;";
        Add(command, "$id", positionId);
        command.ExecuteNonQuery();
    }

    private static void InsertTrade(
        SqliteConnection connection,
        SqliteTransaction tx,
        long accountId,
        string symbol,
        string companyName,
        string side,
        decimal quantity,
        decimal priceMxn,
        decimal quotePrice,
        string currency,
        decimal? fxRate,
        decimal gross,
        decimal commission,
        decimal cashAfter,
        decimal averageAfter,
        decimal quantityAfter,
        decimal? realizedPnl)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO Trade (
                AccountId, Symbol, CompanyName, Side, Quantity, PriceMxn, QuotePrice, QuoteCurrency,
                FxRate, GrossMxn, CommissionMxn, CashAfter, AverageCostAfter, QuantityAfter, CreatedAt, RealizedPnlMxn)
            VALUES (
                $account, $symbol, $company, $side, $quantity, $price, $quote, $currency,
                $fx, $gross, $commission, $cash, $average, $afterQty, $created, $realized);
            """;
        Add(command, "$account", accountId);
        Add(command, "$symbol", symbol);
        Add(command, "$company", companyName);
        Add(command, "$side", side);
        Add(command, "$quantity", Store(quantity));
        Add(command, "$price", Store(priceMxn));
        Add(command, "$quote", Store(quotePrice));
        Add(command, "$currency", currency);
        Add(command, "$fx", fxRate is null ? null : Store(fxRate.Value));
        Add(command, "$gross", Store(gross));
        Add(command, "$commission", Store(commission));
        Add(command, "$cash", Store(cashAfter));
        Add(command, "$average", Store(averageAfter));
        Add(command, "$afterQty", Store(quantityAfter));
        Add(command, "$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        Add(command, "$realized", realizedPnl is null ? null : Store(realizedPnl.Value));
        command.ExecuteNonQuery();
    }

    private static void InsertCashMovement(
        SqliteConnection connection,
        SqliteTransaction tx,
        long accountId,
        string kind,
        decimal amount,
        decimal cashAfter,
        string? note)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO CashMovement (AccountId, Kind, AmountMxn, CashAfter, Note, CreatedAt)
            VALUES ($account, $kind, $amount, $cash, $note, $created);
            """;
        Add(command, "$account", accountId);
        Add(command, "$kind", kind);
        Add(command, "$amount", Store(amount));
        Add(command, "$cash", Store(cashAfter));
        Add(command, "$note", string.IsNullOrWhiteSpace(note) ? "" : note.Trim());
        Add(command, "$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static decimal NetDeposits(SqliteConnection connection, SqliteTransaction? tx, long accountId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT Kind, AmountMxn FROM CashMovement WHERE AccountId = $account;
            """;
        Add(command, "$account", accountId);
        using var reader = command.ExecuteReader();
        decimal net = 0;
        while (reader.Read())
        {
            var amount = Load(reader, 1);
            net += CashMovementKinds.IsDeposit(reader.GetString(0)) ? amount : -amount;
        }
        return net;
    }

    public bool HasEquitySnapshot(long accountId, DateOnly asOfDate)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1 FROM EquitySnapshot
                WHERE AccountId = $account AND AsOfDate = $date
                LIMIT 1;
                """;
            Add(command, "$account", accountId);
            Add(command, "$date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return command.ExecuteScalar() is not null;
        }
    }

    public void UpsertEquitySnapshot(
        long accountId,
        DateOnly asOfDate,
        decimal equityMxn,
        decimal marketValueMxn,
        decimal cashMxn,
        bool overwrite)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            if (overwrite)
            {
                command.CommandText = """
                    INSERT INTO EquitySnapshot (AccountId, AsOfDate, EquityMxn, MarketValueMxn, CashMxn, CapturedAt)
                    VALUES ($account, $date, $equity, $market, $cash, $captured)
                    ON CONFLICT(AccountId, AsOfDate) DO UPDATE SET
                        EquityMxn = excluded.EquityMxn,
                        MarketValueMxn = excluded.MarketValueMxn,
                        CashMxn = excluded.CashMxn,
                        CapturedAt = excluded.CapturedAt;
                    """;
            }
            else
            {
                command.CommandText = """
                    INSERT OR IGNORE INTO EquitySnapshot (AccountId, AsOfDate, EquityMxn, MarketValueMxn, CashMxn, CapturedAt)
                    VALUES ($account, $date, $equity, $market, $cash, $captured);
                    """;
            }

            Add(command, "$account", accountId);
            Add(command, "$date", asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(command, "$equity", Store(equityMxn));
            Add(command, "$market", Store(marketValueMxn));
            Add(command, "$cash", Store(cashMxn));
            Add(command, "$captured", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<EquitySnapshotData> ListEquitySnapshots(long accountId)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, AccountId, AsOfDate, EquityMxn, MarketValueMxn, CashMxn, CapturedAt
                FROM EquitySnapshot
                WHERE AccountId = $account
                ORDER BY AsOfDate;
                """;
            Add(command, "$account", accountId);
            using var reader = command.ExecuteReader();
            var list = new List<EquitySnapshotData>();
            while (reader.Read())
            {
                list.Add(new EquitySnapshotData
                {
                    Id = reader.GetInt64(0),
                    AccountId = reader.GetInt64(1),
                    AsOfDate = DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    EquityMxn = Load(reader, 3),
                    MarketValueMxn = Load(reader, 4),
                    CashMxn = Load(reader, 5),
                    CapturedAt = ParseDate(reader.GetString(6))
                });
            }

            return list;
        }
    }

    public IReadOnlyDictionary<string, (int Position, bool Visible, string? Title, string? Color)> LoadPortfolioColumnLayout()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Position, Visible, Title, Color FROM PortfolioColumnLayout;";
            var layout = new Dictionary<string, (int Position, bool Visible, string? Title, string? Color)>(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var title = reader.IsDBNull(3) ? null : reader.GetString(3);
                var color = reader.IsDBNull(4) ? null : reader.GetString(4);
                layout[reader.GetString(0)] = (
                    checked((int)reader.GetInt64(1)),
                    reader.GetInt64(2) != 0,
                    string.IsNullOrWhiteSpace(title) ? null : title,
                    string.IsNullOrWhiteSpace(color) ? null : color);
            }

            return layout;
        }
    }

    public void SavePortfolioColumnLayout(IReadOnlyList<(string Key, int Position, bool Visible, string? Title, string? Color)> columns)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var transaction = connection.BeginTransaction();
            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM PortfolioColumnLayout;";
                clear.ExecuteNonQuery();
            }

            foreach (var column in columns)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO PortfolioColumnLayout (Key, Position, Visible, Title, Color)
                    VALUES ($key, $position, $visible, $title, $color);
                    """;
                Add(command, "$key", column.Key);
                Add(command, "$position", column.Position);
                Add(command, "$visible", column.Visible ? 1 : 0);
                Add(command, "$title", string.IsNullOrWhiteSpace(column.Title) ? null : column.Title.Trim());
                Add(command, "$color", string.IsNullOrWhiteSpace(column.Color) ? null : column.Color);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void UpsertQuoteCache(PesosQuote quote)
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO QuoteCache (
                    Symbol, CompanyName, Exchange, Currency, Instrument,
                    PriceOriginal, PreviousOriginal, PriceMxn, PreviousMxn, FxRate, InPesos, MarketOpen,
                    SessionOpenOriginal, SessionOpenMxn, DayLowOriginal, DayHighOriginal, DayLowMxn, DayHighMxn,
                    FiftyTwoWeekLowOriginal, FiftyTwoWeekHighOriginal, FiftyTwoWeekLowMxn, FiftyTwoWeekHighMxn, FetchedAt)
                VALUES (
                    $symbol, $company, $exchange, $currency, $instrument,
                    $price, $previous, $priceMxn, $previousMxn, $fx, $inPesos, $open,
                    $sessionOpen, $sessionOpenMxn, $dayLow, $dayHigh, $dayLowMxn, $dayHighMxn,
                    $weekLow, $weekHigh, $weekLowMxn, $weekHighMxn, $fetched)
                ON CONFLICT(Symbol) DO UPDATE SET
                    CompanyName = excluded.CompanyName,
                    Exchange = excluded.Exchange,
                    Currency = excluded.Currency,
                    Instrument = excluded.Instrument,
                    PriceOriginal = excluded.PriceOriginal,
                    PreviousOriginal = excluded.PreviousOriginal,
                    PriceMxn = excluded.PriceMxn,
                    PreviousMxn = excluded.PreviousMxn,
                    FxRate = excluded.FxRate,
                    InPesos = excluded.InPesos,
                    MarketOpen = excluded.MarketOpen,
                    SessionOpenOriginal = excluded.SessionOpenOriginal,
                    SessionOpenMxn = excluded.SessionOpenMxn,
                    DayLowOriginal = excluded.DayLowOriginal,
                    DayHighOriginal = excluded.DayHighOriginal,
                    DayLowMxn = excluded.DayLowMxn,
                    DayHighMxn = excluded.DayHighMxn,
                    FiftyTwoWeekLowOriginal = excluded.FiftyTwoWeekLowOriginal,
                    FiftyTwoWeekHighOriginal = excluded.FiftyTwoWeekHighOriginal,
                    FiftyTwoWeekLowMxn = excluded.FiftyTwoWeekLowMxn,
                    FiftyTwoWeekHighMxn = excluded.FiftyTwoWeekHighMxn,
                    FetchedAt = excluded.FetchedAt;
                """;
            Add(command, "$symbol", quote.Symbol);
            Add(command, "$company", quote.CompanyName);
            Add(command, "$exchange", quote.Exchange);
            Add(command, "$currency", quote.Currency);
            Add(command, "$instrument", quote.Instrument);
            Add(command, "$price", Store(quote.PriceOriginal));
            Add(command, "$previous", quote.PreviousOriginal is null ? null : Store(quote.PreviousOriginal.Value));
            Add(command, "$priceMxn", Store(quote.PriceMxn));
            Add(command, "$previousMxn", quote.PreviousMxn is null ? null : Store(quote.PreviousMxn.Value));
            Add(command, "$fx", quote.FxRate is null ? null : Store(quote.FxRate.Value));
            Add(command, "$inPesos", quote.InPesos ? 1 : 0);
            Add(command, "$open", quote.MarketOpen ? 1 : 0);
            Add(command, "$sessionOpen", Opt(quote.SessionOpenOriginal));
            Add(command, "$sessionOpenMxn", Opt(quote.SessionOpenMxn));
            Add(command, "$dayLow", Opt(quote.DayLowOriginal));
            Add(command, "$dayHigh", Opt(quote.DayHighOriginal));
            Add(command, "$dayLowMxn", Opt(quote.DayLowMxn));
            Add(command, "$dayHighMxn", Opt(quote.DayHighMxn));
            Add(command, "$weekLow", Opt(quote.FiftyTwoWeekLowOriginal));
            Add(command, "$weekHigh", Opt(quote.FiftyTwoWeekHighOriginal));
            Add(command, "$weekLowMxn", Opt(quote.FiftyTwoWeekLowMxn));
            Add(command, "$weekHighMxn", Opt(quote.FiftyTwoWeekHighMxn));
            Add(command, "$fetched", quote.FetchedAtUtc.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<string, PesosQuote> LoadQuoteCache()
    {
        lock (_gate)
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Symbol, CompanyName, Exchange, Currency, Instrument,
                       PriceOriginal, PreviousOriginal, PriceMxn, PreviousMxn, FxRate, InPesos, MarketOpen,
                       SessionOpenOriginal, SessionOpenMxn, DayLowOriginal, DayHighOriginal, DayLowMxn, DayHighMxn,
                       FiftyTwoWeekLowOriginal, FiftyTwoWeekHighOriginal, FiftyTwoWeekLowMxn, FiftyTwoWeekHighMxn, FetchedAt
                FROM QuoteCache;
                """;
            using var reader = command.ExecuteReader();
            var map = new Dictionary<string, PesosQuote>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var quote = new PesosQuote
                {
                    Symbol = reader.GetString(0),
                    CompanyName = reader.GetString(1),
                    Exchange = reader.GetString(2),
                    Currency = reader.GetString(3),
                    Instrument = reader.GetString(4),
                    PriceOriginal = Load(reader, 5),
                    PreviousOriginal = reader.IsDBNull(6) ? null : Load(reader, 6),
                    PriceMxn = Load(reader, 7),
                    PreviousMxn = reader.IsDBNull(8) ? null : Load(reader, 8),
                    FxRate = reader.IsDBNull(9) ? null : Load(reader, 9),
                    InPesos = reader.GetInt64(10) != 0,
                    MarketOpen = reader.GetInt64(11) != 0,
                    SessionOpenOriginal = reader.IsDBNull(12) ? null : Load(reader, 12),
                    SessionOpenMxn = reader.IsDBNull(13) ? null : Load(reader, 13),
                    DayLowOriginal = reader.IsDBNull(14) ? null : Load(reader, 14),
                    DayHighOriginal = reader.IsDBNull(15) ? null : Load(reader, 15),
                    DayLowMxn = reader.IsDBNull(16) ? null : Load(reader, 16),
                    DayHighMxn = reader.IsDBNull(17) ? null : Load(reader, 17),
                    FiftyTwoWeekLowOriginal = reader.IsDBNull(18) ? null : Load(reader, 18),
                    FiftyTwoWeekHighOriginal = reader.IsDBNull(19) ? null : Load(reader, 19),
                    FiftyTwoWeekLowMxn = reader.IsDBNull(20) ? null : Load(reader, 20),
                    FiftyTwoWeekHighMxn = reader.IsDBNull(21) ? null : Load(reader, 21),
                    FetchedAtUtc = ParseDate(reader.GetString(22)),
                    IsStale = true
                };
                map[quote.Symbol] = quote;
            }

            return map;
        }
    }

    private static string? Opt(decimal? value) => value is null ? null : Store(value.Value);

    private static void Add(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string Store(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static decimal Load(SqliteDataReader reader, int ordinal) =>
        decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
