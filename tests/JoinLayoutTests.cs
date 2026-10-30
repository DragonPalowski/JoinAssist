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
        foreach(string sample in new[]{
            "SELECT * FROM appo|intments_tb",
            "SELECT appo|intments_tb.Id FROM appointments_tb",
            "SELECT a.Id FROM appointments_tb a WHERE a.Cust|omerId=1",
            "SELECT * FROM Orders JOIN Cust|omers ON Orders.CustomerId=Customers.Id",
            "SELECT * FROM Orders o JOIN Customers c ON o.Cust|omerId=c.Id",
            "UPDATE Orders SET Cust|omerId=1 WHERE Id=2",
            "DELETE FROM Orders WHERE Cust|omerId=1",
            "INSERT INTO Orders(Cust|omerId) VALUES(1)",
            "sel|ect * FROM Orders",
            "SELECT * FROM Orders WHERE Name li|ke",
            "CREATE TABLE #Orders(Id int); SELECT * FROM #Or|ders",
            "SELECT * FROM [appo|intments_tb]",
            "SELECT * FROM \"appo|intments_tb\"",
            "SELECT o.|CustomerId FROM Orders o",
            "SELECT * FROM |Orders"
        })Case(sample,false);
        Case("SELECT * FROM appo|",true);
        Case("SELECT o.Cust| FROM Orders o",true);
        Case("SELECT o.Cust|, o.Id FROM Orders o",true);
        Case("SELECT * FROM Orders WHERE Name like|",true);
        Console.WriteLine("PASS: " + checks + " JOIN layout checks (blank/indentation, existing following JOINs, typed names/ON, later WHERE and explicit fallback).");
    }
}
