using System;
using JoinAssist;

static class JoinLayoutTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new Exception("JOIN layout: " + message);
    }

    static void Case(string sample, bool automatic, bool manual = true)
    {
        int caret = sample.IndexOf('|');
        string sql = sample.Remove(caret, 1);
        var trigger = AutomaticTrigger.TryCreate(sql, caret);
        Check(!manual || trigger != null, "explicit request remains available: " + sample);
        Check((trigger != null && trigger.AllowsAutomatic(sql, caret)) == automatic, "automatic layout policy: " + sample);
    }

    internal static void Run()
    {
        foreach (string layout in new[]
        {
            " ",
            "\t",
            " \t ",
            "\r\n\t",
            "\n    "
        }

        )
            Case("SELECT * FROM Orders INNER JOIN" + layout + "|", false);
        Case("SELECT * FROM Orders JOIN Cu |", false,false);
        Case("SELECT * FROM Orders JOIN \n   Cu|", true);
        Case("SELECT * FROM Orders JOIN Cu|", true);
        Case("SELECT * FROM Orders JOIN [Cu|", true);
        Case("SELECT * FROM Orders JOIN dbo.Cu|", true);
        foreach (string next in new[]
        {
            "JOIN",
            "INNER JOIN",
            "LEFT JOIN",
            "RIGHT OUTER JOIN",
            "FULL OUTER JOIN",
            "CROSS JOIN"
        }

        )
            Case("SELECT * FROM Orders o JOIN Cu|\n" + next + " Items i" + (next == "CROSS JOIN" ? "" : " ON o.Id=i.OrderId"), false, false);
        Case("SELECT * FROM Orders o JOIN Cu| /* keep */ LEFT JOIN Items i ON o.Id=i.OrderId", false);
        Case("SELECT * FROM Orders o JOIN Cu|\nWHERE o.Id=1 ORDER BY o.Id", true);
        Case("SELECT * FROM Orders o JOIN Cu| -- JOIN in comment\nWHERE o.Id=1", true);
        Case("SELECT * FROM Orders JOIN Cu|; SELECT * FROM Items JOIN Orders ON Items.OrderId=Orders.Id", true);
        Case("SELECT * FROM Orders o JOIN Customers c ON |", false);
        Case("SELECT * FROM Orders o JOIN Customers c ON\n\t|", false);
        Case("SELECT * FROM Orders o JOIN Customers c ON o.Cust|", true);
        Case("SELECT * FROM Orders o JOIN Customers c ON o.|", true);
        Case("SELECT * FROM |", true);
        Case("SELECT * FROM Orders WHERE Cu|", true);
        Console.WriteLine("PASS: " + checks + " JOIN layout checks (blank/indentation, existing following JOINs, typed names/ON, later WHERE and explicit fallback).");
    }
}
