using System.IO;
using Microsoft.Data.Sqlite;

namespace SimTrade.Data;

public sealed class AppDatabase
{
    private static string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimTrade",
        "simtrade.db");

    public static string DatabasePath => _databasePath;

    /// <summary>Solo para pruebas unitarias: apunta a un archivo temporal.</summary>
    public static void UseDatabasePath(string path) =>
        _databasePath = path ?? throw new ArgumentNullException(nameof(path));

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS Account (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                InitialCash TEXT NOT NULL,
                Cash TEXT NOT NULL,
                BuyCommissionPercent TEXT NOT NULL,
                SellCommissionPercent TEXT NOT NULL,
                MarketKind TEXT NOT NULL DEFAULT 'Stocks',
                Icon TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Position (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                Symbol TEXT NOT NULL COLLATE NOCASE,
                CompanyName TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                AverageCost TEXT NOT NULL,
                TotalCost TEXT NOT NULL,
                OpenedAt TEXT,
                UNIQUE (AccountId, Symbol),
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Trade (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                Symbol TEXT NOT NULL,
                CompanyName TEXT NOT NULL,
                Side TEXT NOT NULL,
                Quantity TEXT NOT NULL,
                PriceMxn TEXT NOT NULL,
                QuotePrice TEXT NOT NULL,
                QuoteCurrency TEXT NOT NULL,
                FxRate TEXT,
                GrossMxn TEXT NOT NULL,
                CommissionMxn TEXT NOT NULL,
                CashAfter TEXT NOT NULL,
                AverageCostAfter TEXT NOT NULL,
                QuantityAfter TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Position_Account ON Position (AccountId);
            CREATE INDEX IF NOT EXISTS IX_Trade_Account ON Trade (AccountId, Id);

            CREATE TABLE IF NOT EXISTS Setting (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PendingOrder (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                Symbol TEXT NOT NULL,
                CompanyName TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Side TEXT NOT NULL,
                OrderType TEXT NOT NULL DEFAULT 'Market',
                Quantity TEXT NOT NULL,
                ReservedMxn TEXT NOT NULL,
                QuotePrice TEXT NOT NULL,
                LimitPriceMxn TEXT,
                FxRate TEXT,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT,
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Pending_Account ON PendingOrder (AccountId, Id);

            CREATE TABLE IF NOT EXISTS EquitySnapshot (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                AsOfDate TEXT NOT NULL,
                EquityMxn TEXT NOT NULL,
                MarketValueMxn TEXT NOT NULL,
                CashMxn TEXT NOT NULL,
                CapturedAt TEXT NOT NULL,
                UNIQUE (AccountId, AsOfDate),
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_EquitySnapshot_Account ON EquitySnapshot (AccountId, AsOfDate);

            CREATE TABLE IF NOT EXISTS CashMovement (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                AmountMxn TEXT NOT NULL,
                CashAfter TEXT NOT NULL,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_CashMovement_Account ON CashMovement (AccountId, Id);
            """;
        command.ExecuteNonQuery();
        EnsureMarketKind(connection);
        EnsureAccountIcon(connection);
        EnsureAccountNotes(connection);
        EnsureOpenedAt(connection);
        EnsureEquitySnapshot(connection);
        EnsurePendingOrderType(connection);
        EnsurePendingExpiresAt(connection);
        EnsureTradeRealizedPnl(connection);
        EnsureTradeNotes(connection);
        EnsureCashMovement(connection);
        EnsureQuoteCache(connection);
        EnsurePortfolioColumnLayout(connection);
    }

    private static void EnsurePortfolioColumnLayout(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PortfolioColumnLayout (
                Key TEXT PRIMARY KEY COLLATE NOCASE,
                Position INTEGER NOT NULL,
                Visible INTEGER NOT NULL,
                Title TEXT,
                Color TEXT
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureTradeNotes(SqliteConnection connection)
    {
        if (HasColumn(connection, "Trade", "Notes"))
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Trade ADD COLUMN Notes TEXT NOT NULL DEFAULT '';";
        alter.ExecuteNonQuery();
    }

    private static void EnsureQuoteCache(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS QuoteCache (
                Symbol TEXT PRIMARY KEY COLLATE NOCASE,
                CompanyName TEXT NOT NULL,
                Exchange TEXT NOT NULL,
                Currency TEXT NOT NULL,
                Instrument TEXT NOT NULL,
                PriceOriginal TEXT NOT NULL,
                PreviousOriginal TEXT,
                PriceMxn TEXT NOT NULL,
                PreviousMxn TEXT,
                FxRate TEXT,
                InPesos INTEGER NOT NULL,
                MarketOpen INTEGER NOT NULL,
                SessionOpenOriginal TEXT,
                SessionOpenMxn TEXT,
                DayLowOriginal TEXT,
                DayHighOriginal TEXT,
                DayLowMxn TEXT,
                DayHighMxn TEXT,
                FiftyTwoWeekLowOriginal TEXT,
                FiftyTwoWeekHighOriginal TEXT,
                FiftyTwoWeekLowMxn TEXT,
                FiftyTwoWeekHighMxn TEXT,
                FetchedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureAccountIcon(SqliteConnection connection)
    {
        if (HasColumn(connection, "Account", "Icon"))
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Account ADD COLUMN Icon TEXT NOT NULL DEFAULT '';";
        alter.ExecuteNonQuery();
    }

    private static void EnsureAccountNotes(SqliteConnection connection)
    {
        if (HasColumn(connection, "Account", "Notes"))
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Account ADD COLUMN Notes TEXT NOT NULL DEFAULT '';";
        alter.ExecuteNonQuery();
    }

    private static void EnsureTradeRealizedPnl(SqliteConnection connection)
    {
        if (HasColumn(connection, "Trade", "RealizedPnlMxn"))
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Trade ADD COLUMN RealizedPnlMxn TEXT;";
        alter.ExecuteNonQuery();
    }

    private static void EnsureCashMovement(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CashMovement (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                AmountMxn TEXT NOT NULL,
                CashAfter TEXT NOT NULL,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_CashMovement_Account ON CashMovement (AccountId, Id);
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsurePendingOrderType(SqliteConnection connection)
    {
        if (!HasColumn(connection, "PendingOrder", "OrderType"))
        {
            using var alterType = connection.CreateCommand();
            alterType.CommandText = "ALTER TABLE PendingOrder ADD COLUMN OrderType TEXT NOT NULL DEFAULT 'Market';";
            alterType.ExecuteNonQuery();
        }

        if (!HasColumn(connection, "PendingOrder", "LimitPriceMxn"))
        {
            using var alterLimit = connection.CreateCommand();
            alterLimit.CommandText = "ALTER TABLE PendingOrder ADD COLUMN LimitPriceMxn TEXT;";
            alterLimit.ExecuteNonQuery();
        }
    }

    private static void EnsurePendingExpiresAt(SqliteConnection connection)
    {
        if (!HasColumn(connection, "PendingOrder", "ExpiresAt"))
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE PendingOrder ADD COLUMN ExpiresAt TEXT;";
            alter.ExecuteNonQuery();
        }

        // Órdenes con precio de activación sin vigencia: 30 días desde su creación.
        using var backfill = connection.CreateCommand();
        backfill.CommandText = """
            UPDATE PendingOrder
            SET ExpiresAt = datetime(CreatedAt, '+30 days')
            WHERE ExpiresAt IS NULL
              AND LOWER(COALESCE(OrderType, 'Market')) IN ('limit', 'stoploss', 'takeprofit');
            """;
        backfill.ExecuteNonQuery();
    }

    private static void EnsureEquitySnapshot(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS EquitySnapshot (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AccountId INTEGER NOT NULL,
                AsOfDate TEXT NOT NULL,
                EquityMxn TEXT NOT NULL,
                MarketValueMxn TEXT NOT NULL,
                CashMxn TEXT NOT NULL,
                CapturedAt TEXT NOT NULL,
                UNIQUE (AccountId, AsOfDate),
                FOREIGN KEY (AccountId) REFERENCES Account (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_EquitySnapshot_Account ON EquitySnapshot (AccountId, AsOfDate);
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureMarketKind(SqliteConnection connection)
    {
        if (HasColumn(connection, "Account", "MarketKind"))
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Account ADD COLUMN MarketKind TEXT NOT NULL DEFAULT 'Stocks';";
        alter.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void EnsureOpenedAt(SqliteConnection connection)
    {
        if (!HasColumn(connection, "Position", "OpenedAt"))
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Position ADD COLUMN OpenedAt TEXT;";
            alter.ExecuteNonQuery();
        }

        using var backfill = connection.CreateCommand();
        backfill.CommandText = """
            UPDATE Position
            SET OpenedAt = (
                SELECT MIN(t.CreatedAt)
                FROM Trade t
                WHERE t.AccountId = Position.AccountId
                  AND t.Symbol = Position.Symbol COLLATE NOCASE
                  AND t.Side = 'BUY'
                  AND t.CreatedAt > COALESCE((
                      SELECT MAX(s.CreatedAt)
                      FROM Trade s
                      WHERE s.AccountId = Position.AccountId
                        AND s.Symbol = Position.Symbol COLLATE NOCASE
                        AND s.Side = 'SELL'
                        AND CAST(s.QuantityAfter AS REAL) = 0
                  ), '')
            )
            WHERE OpenedAt IS NULL;
            """;
        backfill.ExecuteNonQuery();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
