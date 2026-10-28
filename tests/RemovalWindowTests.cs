using System;
using System.Linq;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

static class RemovalWindowTests
{
    static int checks;
    static Type targetType, dialogType;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new Exception("Uninstall UI: " + message);
    }

    static object Field(object source, string name)
    {
        return source.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source);
    }

    static Delegate Reader(Func<Task> read)
    {
        var returnType = typeof(Task<>).MakeGenericType(targetType);
        var call = Expression.Invoke(Expression.Constant(read));
        return Expression.Lambda(typeof(Func<>).MakeGenericType(returnType), Expression.Convert(call, returnType)).Compile();
    }

    static Task Completed(object target)
    {
        return (Task)typeof(Task).GetMethods().Single(m => m.Name == "FromResult" && m.IsGenericMethod).MakeGenericMethod(targetType).Invoke(null, new[] { target });
    }

    static Window Window(Func<Task> read, Func<Task<bool>> pending, Action<object> launched)
    {
        var parameter = Expression.Parameter(targetType, "target");
        var launch = Expression.Lambda(typeof(Action<>).MakeGenericType(targetType), Expression.Invoke(Expression.Constant(launched), Expression.Convert(parameter, typeof(object))), parameter).Compile();
        return (Window)Activator.CreateInstance(dialogType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Reader(read), pending, launch }, null);
    }

    static Task Prepare(Window window, bool launch)
    {
        return (Task)dialogType.GetMethod("PrepareAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { launch });
    }

    internal static void Run(Assembly plugin)
    {
        targetType = plugin.GetType("JoinAssist.RemovalTarget");
        dialogType = plugin.GetType("JoinAssist.UninstallDialog");
        var target = Activator.CreateInstance(targetType, true);
        targetType.GetField("Host", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, @"C:\SyntheticSSMS\Common7\IDE\SSMS.exe");
        int reads = 0, starts = 0;
        var window = Window(() =>
        {
            reads++;
            return Completed(target);
        }, () => Task.FromResult(false), t => starts++);
        try
        {
            Check(reads == 0 && starts == 0 && !((Button)Field(window, "remove")).IsEnabled, "construction never runs installer or reads setup");
            Prepare(window, false).GetAwaiter().GetResult();
            Check(reads == 1 && starts == 0 && ((Button)Field(window, "remove")).IsEnabled, "inspection enables explicit removal without launching");
            Check(((TextBlock)Field(window, "status")).Text.Contains("utente corrente"), "per-user scope is visible");
            foreach (string theme in new[]
            {
                "dark",
                "light"
            }

            )
            {
                var background = theme == "dark" ? new SolidColorBrush(Color.FromRgb(37, 37, 38)) : Brushes.White;
                var foreground = theme == "dark" ? Brushes.WhiteSmoke : Brushes.Black;
                window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey] = background;
                window.Resources[EnvironmentColors.ToolWindowTextBrushKey] = foreground;
                NativeMenuTests.Preview(window, "uninstall-" + theme + ".png");
                Check(((SolidColorBrush)((TextBlock)Field(window, "status")).Foreground).Color == foreground.Color, "status follows " + theme + " theme");
            }

            Prepare(window, true).GetAwaiter().GetResult();
            Check(reads == 2 && starts == 1 && !((Button)Field(window, "remove")).IsEnabled, "click re-identifies target then launches once");
            Prepare(window, true).GetAwaiter().GetResult();
            Check(starts == 1, "duplicate click cannot launch twice");
        }
        finally
        {
            window.Close();
        }

        reads = starts = 0;
        window = Window(() =>
        {
            reads++;
            return Completed(target);
        }, () => Task.FromResult(true), t => starts++);
        try
        {
            Prepare(window, true).GetAwaiter().GetResult();
            Check(starts == 0 && reads == 0 && !((Button)Field(window, "remove")).IsEnabled, "pending update blocks removal before discovery");
            Check(((TextBlock)Field(window, "status")).Text.Contains("aggiornamento"), "pending update explanation");
        }
        finally
        {
            window.Close();
        }

        var changed = Activator.CreateInstance(targetType, true);
        targetType.GetField("Host", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(changed, @"C:\SyntheticSSMS\Common7\IDE\SSMS.exe");
        targetType.GetField("AllUsers", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(changed, true);
        int scopeReads = 0;
        starts = 0;
        window = Window(() => Completed(++scopeReads == 1 ? target : changed), () => Task.FromResult(false), t => starts++);
        try
        {
            Prepare(window, false).GetAwaiter().GetResult();
            Prepare(window, true).GetAwaiter().GetResult();
            Check(starts == 0 && !((Button)Field(window, "remove")).IsEnabled, "scope changed since preview requires a new inspection");
        }
        finally
        {
            window.Close();
        }

        int inspections = 0;
        starts = 0;
        window = Window(() => Completed(target), () => Task.FromResult(++inspections > 2), t => starts++);
        try
        {
            Prepare(window, false).GetAwaiter().GetResult();
            Prepare(window, true).GetAwaiter().GetResult();
            Check(starts == 0 && !((Button)Field(window, "remove")).IsEnabled, "update scheduled during discovery blocks actual click");
        }
        finally
        {
            window.Close();
        }

        window = Window(() =>
        {
            throw new InvalidOperationException("Synthetic missing installer");
        }, () => Task.FromResult(false), t => starts++);
        try
        {
            Prepare(window, false).GetAwaiter().GetResult();
            Check(!((Button)Field(window, "remove")).IsEnabled && ((Button)Field(window, "retry")).IsEnabled, "discovery failure offers retry, no removal");
        }
        finally
        {
            window.Close();
        }

        var source = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(targetType));
        var pendingTask = (Task)source.GetType().GetProperty("Task").GetValue(source);
        starts = 0;
        window = Window(() => pendingTask, () => Task.FromResult(false), t => starts++);
        var preparing = Prepare(window, true);
        window.Close();
        source.GetType().GetMethod("SetResult").Invoke(source, new[] { target });
        // Completed synchronous continuations are the normal test route; pump only if needed.
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!preparing.IsCompleted)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                throw new Exception("Pending uninstall test timed out");
            var tick = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(new Action(() => tick.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(tick);
        }

        preparing.GetAwaiter().GetResult();
        Check(starts == 0, "closing during discovery prevents later uninstall launch");
        Console.WriteLine("PASS: " + checks + " uninstall UI checks (explicit action, scope, retry, pending updates, close races and themes). Synthetic delegates only; no process or installer started.");
    }
}
