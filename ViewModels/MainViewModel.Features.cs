using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SimTrade.Data;
using SimTrade.Models;
using SimTrade.Services;

namespace SimTrade.ViewModels;

public partial class MainViewModel
{
    partial void OnHistorySymbolFilterChanged(string value) => ApplyHistoryFilter();
    partial void OnHistorySideFilterChanged(string value) => ApplyHistoryFilter();
    partial void OnHistoryFromChanged(DateTime? value) => ApplyHistoryFilter();
    partial void OnHistoryToChanged(DateTime? value) => ApplyHistoryFilter();
    partial void OnHistoryPageChanged(int value) => RefreshHistoryPage();

    private static (DateTime From, DateTime To) CurrentMonthRange()
    {
        var today = DateTime.Today;
        var from = new DateTime(today.Year, today.Month, 1);
        var to = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        return (from, to);
    }

    private void ApplyHistoryFilter()
    {
        _historyMatches.Clear();
        DateTime? from = HistoryFrom?.Date;
        DateTime? to = HistoryTo?.Date.AddDays(1).AddTicks(-1);

        var symbol = HistorySymbolFilter.Trim();
        foreach (var trade in Trades)
        {
            if (!string.IsNullOrWhiteSpace(symbol)
                && trade.SymbolKey.IndexOf(symbol, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (HistorySideFilter.Equals("Compras", StringComparison.OrdinalIgnoreCase)
                && trade.SideKey != "BUY")
                continue;
            if (HistorySideFilter.Equals("Ventas", StringComparison.OrdinalIgnoreCase)
                && trade.SideKey != "SELL")
                continue;
            if (HistorySideFilter.Equals("Aportaciones", StringComparison.OrdinalIgnoreCase)
                && trade.SideKey != "DEPOSIT")
                continue;
            if (HistorySideFilter.Equals("Retiros", StringComparison.OrdinalIgnoreCase)
                && trade.SideKey != "WITHDRAW")
                continue;

            var local = trade.CreatedAt.ToLocalTime();
            if (from is DateTime start && local < start)
                continue;
            if (to is DateTime end && local > end)
                continue;

            _historyMatches.Add(trade);
        }

        if (HistoryPage > HistoryPageCount)
            HistoryPage = HistoryPageCount;
        if (HistoryPage < 1)
            HistoryPage = 1;

        RefreshHistoryPage();
        OnPropertyChanged(nameof(HasTrades));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(ShowEmptyHistory));
        OnPropertyChanged(nameof(ShowEmptyFilteredHistory));
        OnPropertyChanged(nameof(HistoryPageCount));
        OnPropertyChanged(nameof(HistoryPageText));
        OnPropertyChanged(nameof(ShowHistoryPager));
        OnPropertyChanged(nameof(CanHistoryPrev));
        OnPropertyChanged(nameof(CanHistoryNext));
    }

    private void RefreshHistoryPage()
    {
        FilteredTrades.Clear();
        if (_historyMatches.Count == 0)
            return;

        var page = Math.Clamp(HistoryPage, 1, HistoryPageCount);
        if (page != HistoryPage)
        {
            HistoryPage = page;
            return;
        }

        var start = (page - 1) * HistoryPageSize;
        var take = Math.Min(HistoryPageSize, _historyMatches.Count - start);
        for (var i = 0; i < take; i++)
            FilteredTrades.Add(_historyMatches[start + i]);

        OnPropertyChanged(nameof(HistoryPageText));
        OnPropertyChanged(nameof(CanHistoryPrev));
        OnPropertyChanged(nameof(CanHistoryNext));
    }

    [RelayCommand]
    private void HistoryPrevPage()
    {
        if (HistoryPage > 1)
            HistoryPage--;
    }

    [RelayCommand]
    private void HistoryNextPage()
    {
        if (HistoryPage < HistoryPageCount)
            HistoryPage++;
    }

    [RelayCommand]
    private void ClearHistoryFilters()
    {
        HistorySymbolFilter = "";
        HistorySideFilter = "Todas";
        var range = CurrentMonthRange();
        HistoryFrom = range.From;
        HistoryTo = range.To;
        HistoryPage = 1;
        ApplyHistoryFilter();
    }

    [RelayCommand]
    private void DepositCash()
    {
        var account = InspectedAccount ?? ActiveAccount;
        if (account is null)
        {
            SetStatus("Elige una cuenta para aportar efectivo.", true);
            return;
        }

        if (!MxFormat.TryParse(CashMoveAmount, out var amount) || amount <= 0)
        {
            SetStatus("Escribe un monto de aportación mayor que cero.", true);
            return;
        }

        var accountId = account.Id;
        var accountName = account.Name;
        var result = _repository.Deposit(accountId, amount, CashMoveNote);
        if (!result.Ok)
        {
            SetStatus(result.Message, true);
            MessageBox.Show(result.Message, "Aportación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CashMoveAmount = "";
        CashMoveNote = "";
        RefreshCashAccounts(accountId);
        SetStatus(result.Message, false);
        Page = AppPage.Portafolio;
        MessageBox.Show(
            result.Message + "\n\nCuenta: " + accountName,
            "Aportación registrada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void WithdrawCash()
    {
        var account = InspectedAccount ?? ActiveAccount;
        if (account is null)
        {
            SetStatus("Elige una cuenta para retirar efectivo.", true);
            return;
        }

        if (!MxFormat.TryParse(CashMoveAmount, out var amount) || amount <= 0)
        {
            SetStatus("Escribe un monto de retiro mayor que cero.", true);
            return;
        }

        // Lee efectivo fresco de la BD (no el valor cacheado de la fila).
        var live = _repository.GetAccount(account.Id);
        if (live is null)
        {
            SetStatus("No se encontró la cuenta.", true);
            return;
        }

        if (amount > live.Cash)
        {
            var message = "El efectivo disponible (" + MxFormat.Money(live.Cash)
                + ") no alcanza para retirar " + MxFormat.Money(amount) + ".";
            SetStatus(message, true);
            MessageBox.Show(message, "Retiro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "¿Retirar " + MxFormat.Money(amount) + " de \"" + account.Name + "\"?\n\n"
            + "Efectivo actual: " + MxFormat.Money(live.Cash) + "\n"
            + "Efectivo después: " + MxFormat.Money(live.Cash - amount),
            "Confirmar retiro",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        var accountId = account.Id;
        var accountName = account.Name;
        var result = _repository.Withdraw(accountId, amount, CashMoveNote);
        if (!result.Ok)
        {
            SetStatus(result.Message, true);
            MessageBox.Show(result.Message, "Retiro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CashMoveAmount = "";
        CashMoveNote = "";
        RefreshCashAccounts(accountId);
        SetStatus(result.Message, false);
        Page = AppPage.Portafolio;
        MessageBox.Show(
            result.Message + "\n\nCuenta: " + accountName,
            "Retiro registrado",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshCashAccounts(long accountId)
    {
        // Activa la cuenta movida para que "Efectivo disponible" del Portafolio coincida.
        ReloadAccounts(accountId);
        InspectedAccount = Accounts.FirstOrDefault(item => item.Id == accountId);

        var data = _repository.GetAccount(accountId);
        if (data is not null)
        {
            ActiveAccount?.Apply(data);
            InspectedAccount?.Apply(data);
        }

        RefreshActiveSnapshot();
        RebuildPerformanceSummaries();
    }

    [RelayCommand]
    private void ExportTradesCsv()
    {
        if (ActiveAccount is null || _historyMatches.Count == 0)
        {
            SetStatus("No hay operaciones para exportar con los filtros actuales.", true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar historial",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "SimTrade_" + ActiveAccount.Name + "_historial.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Fecha,Operacion,Simbolo,Empresa,Cantidad,PrecioMXN,Importe,Comision,ResultadoRealizado,EfectivoDespues,CostoPromedio");
            foreach (var trade in _historyMatches)
            {
                sb.Append(Csv(trade.When)).Append(',')
                    .Append(Csv(trade.Side)).Append(',')
                    .Append(Csv(trade.Symbol)).Append(',')
                    .Append(Csv(trade.Company)).Append(',')
                    .Append(Csv(trade.Quantity)).Append(',')
                    .Append(Csv(trade.Price)).Append(',')
                    .Append(Csv(trade.Gross)).Append(',')
                    .Append(Csv(trade.Commission)).Append(',')
                    .Append(Csv(trade.Realized)).Append(',')
                    .Append(Csv(trade.CashAfter)).Append(',')
                    .Append(Csv(trade.AverageAfter))
                    .AppendLine();
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus("Historial exportado a " + dialog.FileName, false);
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo exportar el CSV. " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void BackupDatabase()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Respaldar base de datos",
            Filter = "SQLite (*.db)|*.db",
            FileName = "simtrade-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".db",
            AddExtension = true,
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.Copy(AppDatabase.DatabasePath, dialog.FileName, overwrite: true);
            SetStatus("Respaldo guardado en " + dialog.FileName, false);
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo respaldar la base de datos. " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void CopyDatabasePath()
    {
        try
        {
            Clipboard.SetText(AppDatabase.DatabasePath);
            SetStatus("Ruta de la base de datos copiada al portapapeles.", false);
        }
        catch (Exception)
        {
            SetStatus("No se pudo copiar la ruta de la base de datos.", true);
        }
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private void RebuildDayRanking()
    {
        var keepId = SelectedPosition?.Id;
        var keepSymbol = SelectedPosition?.Symbol;

        DayRanking.Clear();
        var ranked = Positions
            .Where(row => row.DayContribution is not null)
            .OrderByDescending(row => row.DayContribution)
            .ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].SetDayRank(i + 1);
            DayRanking.Add(ranked[i]);
        }

        foreach (var row in Positions.Where(item => item.DayContribution is null))
            row.SetDayRank(0);

        RestoreSelectedPosition(keepId, keepSymbol);
        OnPropertyChanged(nameof(HasDayRanking));
        OnPropertyChanged(nameof(ShowEmptyDayRanking));
    }

    private void RebuildTradingStats()
    {
        if (ActiveAccount is null)
        {
            ClearTradingStats();
            return;
        }

        var trades = _repository.ListTrades(ActiveAccount.Id);
        var equity = _repository.ListEquitySnapshots(ActiveAccount.Id);
        var stats = TradingStats.Compute(trades, equity);
        StatsClosedTradesText = stats.ClosedTrades.ToString(MxFormat.Culture);
        StatsWinRateText = stats.WinRatePercent.ToString("0.00", MxFormat.Culture) + "%";
        StatsWinsText = stats.Wins.ToString(MxFormat.Culture);
        StatsLossesText = stats.Losses.ToString(MxFormat.Culture);
        StatsAverageWinText = MxFormat.SignedMoney(stats.AverageWin);
        StatsAverageLossText = MxFormat.SignedMoney(stats.AverageLoss);
        StatsAveragePnlText = MxFormat.SignedMoney(stats.AveragePnl);
        StatsTotalRealizedText = MxFormat.SignedMoney(stats.TotalRealized);
        StatsTotalRealizedTone = stats.TotalRealized > 0 ? "Up" : stats.TotalRealized < 0 ? "Down" : "Flat";
        StatsMaxDrawdownText = MxFormat.Money(stats.MaxDrawdownMoney) + "  ("
                               + MxFormat.SignedPercent(-stats.MaxDrawdownPercent).TrimStart('+') + ")";
        StatsBestMonthText = stats.BestMonth is null
            ? "—"
            : stats.BestMonth.Label + "  " + MxFormat.SignedMoney(stats.BestMonth.RealizedPnl);
        StatsWorstMonthText = stats.WorstMonth is null
            ? "—"
            : stats.WorstMonth.Label + "  " + MxFormat.SignedMoney(stats.WorstMonth.RealizedPnl);
        HasTradingStats = stats.ClosedTrades > 0 || equity.Count > 1;
        OnPropertyChanged(nameof(ShowEmptyTradingStats));
    }

    private void ClearTradingStats()
    {
        StatsClosedTradesText = "—";
        StatsWinRateText = "—";
        StatsWinsText = "—";
        StatsLossesText = "—";
        StatsAverageWinText = "—";
        StatsAverageLossText = "—";
        StatsAveragePnlText = "—";
        StatsTotalRealizedText = "—";
        StatsTotalRealizedTone = "Flat";
        StatsMaxDrawdownText = "—";
        StatsBestMonthText = "—";
        StatsWorstMonthText = "—";
        HasTradingStats = false;
        OnPropertyChanged(nameof(ShowEmptyTradingStats));
    }

    [RelayCommand]
    private void EditTradeNote(TradeRow? row)
    {
        if (row is null || !row.CanEditNotes)
            return;

        var edited = JournalNoteWindow.Prompt(row.Symbol, row.Side, row.When, row.Notes);
        if (edited is null)
            return;

        var result = _repository.UpdateTradeNotes(row.TradeId, edited);
        SetStatus(result.Message, !result.Ok);
        if (!result.Ok)
            return;

        row.Notes = edited.Trim();
        ApplyHistoryFilter();
        RebuildTradingStats();
    }
}
