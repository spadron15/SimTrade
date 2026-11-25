namespace SimTrade.Services;

public static class NasdaqClock
{
    private static readonly TimeSpan Open = new(9, 30, 0);
    private static readonly TimeSpan RegularClose = new(16, 0, 0);
    private static readonly TimeSpan EarlyClose = new(13, 0, 0);

    public static string Describe(DateTimeOffset now)
    {
        TimeZoneInfo eastern;
        try
        {
            eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return "NASDAQ";
        }
        catch (InvalidTimeZoneException)
        {
            return "NASDAQ";
        }

        var local = TimeZoneInfo.ConvertTime(now, eastern);
        var day = DateOnly.FromDateTime(local.DateTime);
        var clock = local.TimeOfDay;

        if (IsTradingDay(day))
        {
            if (clock < Open)
                return Phrase(false, At(local, Open) - local);

            var close = ClosesEarly(day) ? EarlyClose : RegularClose;
            if (clock < close)
                return Phrase(true, At(local, close) - local);
        }

        var next = NextTradingDay(day.AddDays(1));
        var open = new DateTimeOffset(
            next.ToDateTime(TimeOnly.FromTimeSpan(Open)),
            eastern.GetUtcOffset(next.ToDateTime(TimeOnly.FromTimeSpan(Open))));
        return Phrase(false, open - local);
    }

    private static string Phrase(bool open, TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        var action = open ? "cierra" : "abre";
        if (remaining.TotalMinutes < 1)
            return $"NASDAQ {action} en menos de 1 min";

        var days = (int)remaining.TotalDays;
        var hours = remaining.Hours;
        var minutes = remaining.Minutes;
        if (days > 0)
            return hours > 0
                ? $"NASDAQ {action} en {days} d {hours} h"
                : $"NASDAQ {action} en {days} d";

        if (hours > 0)
            return minutes > 0
                ? $"NASDAQ {action} en {hours} h {minutes} min"
                : $"NASDAQ {action} en {hours} h";

        return $"NASDAQ {action} en {minutes} min";
    }

    private static DateTimeOffset At(DateTimeOffset local, TimeSpan clock) =>
        new(local.Year, local.Month, local.Day, clock.Hours, clock.Minutes, 0, local.Offset);

    private static DateOnly NextTradingDay(DateOnly start)
    {
        var day = start;
        for (var i = 0; i < 14; i++)
        {
            if (IsTradingDay(day))
                return day;
            day = day.AddDays(1);
        }

        return start;
    }

    private static bool IsTradingDay(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        return !Holidays(day.Year).Contains(day);
    }

    private static bool ClosesEarly(DateOnly day)
    {
        var thanksgiving = NthWeekday(day.Year, 11, DayOfWeek.Thursday, 4);
        if (day == DateOnly.FromDateTime(thanksgiving.AddDays(1)))
            return true;

        return day is { Month: 12, Day: 24 } && IsTradingDay(day);
    }

    private static HashSet<DateOnly> Holidays(int year)
    {
        var dates = new HashSet<DateOnly>
        {
            Observed(new DateTime(year, 1, 1)),
            DateOnly.FromDateTime(NthWeekday(year, 1, DayOfWeek.Monday, 3)),
            DateOnly.FromDateTime(NthWeekday(year, 2, DayOfWeek.Monday, 3)),
            DateOnly.FromDateTime(EasterSunday(year).AddDays(-2)),
            DateOnly.FromDateTime(LastWeekday(year, 5, DayOfWeek.Monday)),
            Observed(new DateTime(year, 6, 19)),
            Observed(new DateTime(year, 7, 4)),
            DateOnly.FromDateTime(NthWeekday(year, 9, DayOfWeek.Monday, 1)),
            DateOnly.FromDateTime(NthWeekday(year, 11, DayOfWeek.Thursday, 4)),
            Observed(new DateTime(year, 12, 25))
        };
        var nextNewYear = Observed(new DateTime(year + 1, 1, 1));
        if (nextNewYear.Year == year)
            dates.Add(nextNewYear);
        return dates;
    }

    private static DateOnly Observed(DateTime holiday)
    {
        var observed = holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(-1),
            DayOfWeek.Sunday => holiday.AddDays(1),
            _ => holiday
        };
        return DateOnly.FromDateTime(observed);
    }

    private static DateTime NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateTime(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (7 * (n - 1)));
    }

    private static DateTime LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }

    private static DateTime EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }
}
