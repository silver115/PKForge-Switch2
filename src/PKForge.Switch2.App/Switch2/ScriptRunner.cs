using System.Globalization;
using PKForge.MauiLite;
using PKForge.MauiLite.Diagnostics;
using SkiaSharp;

namespace PKForge.Switch2;

/// <summary>
/// Runs a script against the app, one command per line (units are window units, times milliseconds):
/// <c>wait ms</c>, <c>tap x y</c>, <c>tap-text text</c> (the centre of the first shown label or button holding it),
/// <c>drag x1 y1 x2 y2 ms</c>, <c>press button</c>, <c>hold button ms</c>, <c>shot name</c>, <c>type text</c> (into the focused text field), <c>expect text</c>, <c>expect-gone text</c>, <c>expect-pad handler-type</c>,
/// <c>page TypeName</c>, <c>tree</c>, <c>choose item</c> and <c>expect-option item</c> (an item of the open menu or tool hub, by
/// its name or the start of it), <c>sweep [except item; item]</c> (every item of the open menu or hub in turn, each opened,
/// checked for UI errors and left with B). Lines starting with # are comments.
/// </summary>
public static class ScriptRunner
{
    static readonly Dictionary<string, ulong> Buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = NpadButton.A, ["B"] = NpadButton.B, ["X"] = NpadButton.X, ["Y"] = NpadButton.Y,
        ["L"] = NpadButton.L, ["R"] = NpadButton.R, ["ZL"] = NpadButton.ZL, ["ZR"] = NpadButton.ZR,
        ["Plus"] = NpadButton.Plus, ["Minus"] = NpadButton.Minus,
        ["Up"] = NpadButton.Up, ["Down"] = NpadButton.Down, ["Left"] = NpadButton.Left, ["Right"] = NpadButton.Right,
    };

    /// <returns>0 when every line passed; 1 at the first that failed, after printing its number and why.</returns>
    public static int Run(LiteRunner runner, ScriptedBackend backend, IReadOnlyList<string> lines, string outDir)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(lines);
        Directory.CreateDirectory(outDir);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            string? failure;
            try
            {
                failure = Step(runner, backend, line, outDir);
            }
            catch (Exception error) when (error is FormatException or IndexOutOfRangeException or KeyNotFoundException)
            {
                failure = $"cannot read '{line}': {error.Message}";
            }
            if (failure is not null)
            {
                Console.Error.WriteLine($"line {i + 1}: {failure}");
                return 1;
            }
        }
        return 0;
    }

    // How long a check waits for what it looks for.
    static readonly TimeSpan ExpectTimeout = TimeSpan.FromSeconds(5);

    static string? Step(LiteRunner runner, ScriptedBackend backend, string line, string outDir)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double Number(int index) => double.Parse(parts[index], CultureInfo.InvariantCulture);
        switch (parts[0])
        {
            case "wait":
                Frames(runner, backend, Number(1));
                return null;
            case "tap":
                Tap(runner, backend, Number(1), Number(2));
                return null;
            case "tap-text":
                var wanted = line["tap-text".Length..].Trim();
                var found = runner.CurrentPage is { } current
                    ? VisibleText.Elements(current).FirstOrDefault(shown => shown.Text.Contains(wanted, StringComparison.Ordinal))
                    : default;
                if (found.Element is null)
                    return $"no shown label or button contains '{wanted}'";
                // On the console the report is the only view of a run: it says what a tap reached.
                Console.WriteLine($"tap-text '{wanted}': {found.Element.GetType().Name} \"{found.Text}\" at {found.Window}");
                Tap(runner, backend, found.Window.Center.X, found.Window.Center.Y);
                return null;
            case "drag":
                Drag(runner, backend, Number(1), Number(2), Number(3), Number(4), Number(5));
                return null;
            case "press":
                backend.Held |= Buttons[parts[1]];
                runner.RunFrame();
                backend.Held &= ~Buttons[parts[1]];
                runner.RunFrame();
                return null;
            case "hold":
                backend.Held |= Buttons[parts[1]];
                Frames(runner, backend, Number(2));
                backend.Held &= ~Buttons[parts[1]];
                runner.RunFrame();
                return null;
            case "shot":
                runner.RunFrame();
                if (backend.LastFrame is not { } frame)
                    return "nothing has been drawn yet";
                using (var file = File.Create(Path.Combine(outDir, parts[1] + ".png")))
                    frame.Encode(file, SKEncodedImageFormat.Png, 100);
                return null;
            case "expect":
                var text = line["expect".Length..].Trim();
                return WaitFor(runner, () => runner.CurrentPage is { } page && VisibleText.Of(page).Any(shown => shown.Contains(text, StringComparison.Ordinal)))
                    ? null
                    : $"no visible text contains '{text}'; seen: {string.Join(" | ", (runner.CurrentPage is { } page ? VisibleText.Of(page) : []).Take(12))}";
            case "type":
                // The keyboard's part, in a host with none: the focused text field takes the text, as from swkbd.
                var typed = line["type".Length..].Trim();
                if (runner.CurrentPage is not { } typing || FocusedField(typing) is not { } field)
                    return "no text field has focus";
                field.Text = typed;
                (field as Microsoft.Maui.Controls.Entry)?.SendCompleted();
                (field as Microsoft.Maui.Controls.Editor)?.SendCompleted();
                return null;
            case "expect-gone":
                var gone = line["expect-gone".Length..].Trim();
                return WaitFor(runner, () => runner.CurrentPage is not { } page || !VisibleText.Of(page).Any(shown => shown.Contains(gone, StringComparison.Ordinal)))
                    ? null
                    : $"visible text still contains '{gone}'";
            case "expect-pad":
                // A menu that paints its own text (PKForge's PadMenu) has none to expect: the controller's top handler names it.
                return WaitFor(runner, () => TopPadHandler()?.GetType().Name == parts[1])
                    ? null
                    : $"the controller goes to {TopPadHandler()?.GetType().Name ?? "nothing"}, not {parts[1]}";
            case "choose":
                // PKForge's menus and tool hubs paint their items: an item is chosen by its name, as a press of A on it.
                var item = line["choose".Length..].Trim();
                if (!WaitFor(runner, () => Choices().Any(choice => Names(choice.Label, item))))
                    return $"no open menu offers '{item}'; it offers: {Offered()}";
                Choices().First(choice => Names(choice.Label, item)).Choose();
                runner.RunFrame();
                return null;
            case "expect-option":
                var option = line["expect-option".Length..].Trim();
                return WaitFor(runner, () => Choices().Any(choice => Names(choice.Label, option)))
                    ? null
                    : $"no open menu offers '{option}'; it offers: {Offered()}";
            case "sweep":
                // sweep [reopen button] [except item; item]: the button opens the menu again when an item left it for a page.
                var rest = line["sweep".Length..].Trim();
                ulong? reopen = null;
                if (rest.StartsWith("reopen ", StringComparison.Ordinal))
                {
                    var words = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    reopen = Buttons[words[1]];
                    rest = words.Length > 2 ? words[2] : "";
                }
                return Sweep(runner, backend, reopen, rest.StartsWith("except ", StringComparison.Ordinal)
                    ? rest["except ".Length..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : []);
            case "page":
                return WaitFor(runner, () => runner.CurrentPageName == parts[1]) ? null : $"the page is {runner.CurrentPageName}, not {parts[1]}";
            case "tree":
                if (runner.CurrentPage is { } shown)
                    PrintTree(shown, 0);
                return null;
            default:
                return $"unknown command '{parts[0]}'";
        }
    }

    // A debugging view of the current page: each element's type, window bounds in units, visibility and, for an image, its source.
    static void PrintTree(Microsoft.Maui.Controls.Element element, int depth)
    {
        if (element is not Microsoft.Maui.Controls.VisualElement visual)
            return;
        var detail = visual switch
        {
            Microsoft.Maui.Controls.Image { Source: Microsoft.Maui.Controls.FileImageSource { File: var file } } => $" file={file}",
            Microsoft.Maui.Controls.Image image => $" source={image.Source?.GetType().Name ?? "none"}",
            Microsoft.Maui.Controls.Label label => $" text=\"{label.Text}\"",
            _ => "",
        };
        Console.WriteLine($"{new string(' ', depth * 2)}{visual.GetType().Name} {visual.Bounds} visible={visual.IsVisible} opacity={visual.Opacity}{detail}");
        foreach (var child in ((Microsoft.Maui.IVisualTreeElement)visual).GetVisualChildren())
            PrintTree((Microsoft.Maui.Controls.Element)child, depth + 1);
    }

    static Microsoft.Maui.Controls.InputView? FocusedField(Microsoft.Maui.Controls.Element element) =>
        element is not Microsoft.Maui.Controls.VisualElement { IsVisible: true } visual
            ? null
            : visual is Microsoft.Maui.Controls.InputView { IsFocused: true } field
                ? field
                : ((Microsoft.Maui.IVisualTreeElement)visual).GetVisualChildren()
                    .Select(child => FocusedField((Microsoft.Maui.Controls.Element)child)).FirstOrDefault(found => found is not null);

    // What a script checks can follow work off the UI thread (a restore, a scan, a prompt), so checks wait for it.
    static bool WaitFor(LiteRunner runner, Func<bool> done)
    {
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (waiting.Elapsed > ExpectTimeout)
                return false;
            runner.RunFrame();
        }
        return true;
    }

    // shortcut: reads GamepadRouter's private stack, since PKForge has no public accessor; add one if this breaks.
    static bool Names(string label, string name) => label == name || label.StartsWith(name, StringComparison.Ordinal);

    static string Offered() => string.Join(" | ", Choices().Select(choice => choice.Label).Take(30));

    // The open PadMenu's or tool hub's items, each with a way to choose it as A would: PKForge has no public accessor,
    // so these read their private fields (shortcut: names checked by the headless tests; add an accessor if they break).
    static List<(string Label, Action Choose)> Choices()
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        if (TopPadHandler() is not { } top)
            return [];
        var type = top.GetType();
        if (type.Name == "PadMenu" && type.GetField("_options", Private)?.GetValue(top) is Array options)
        {
            var highlight = type.GetMethod("Highlight", Private)!;
            var choose = type.GetMethod("Choose", Private)!;
            return [.. options.Cast<object>().Select((option, index) => (
                (string)option.GetType().GetProperty("Label")!.GetValue(option)!,
                (Action)(() => { highlight.Invoke(top, [index]); choose.Invoke(top, null); })))];
        }
        if (type.Name == "ToolHubScreen" && type.GetField("_groups", Private)?.GetValue(top) is System.Collections.IEnumerable groups)
        {
            var tab = type.GetField("_tab", Private)!;
            var cursor = type.GetField("_cursor", Private)!;
            var open = type.GetMethod("Open", Private)!;
            var tiles = new List<(string, Action)>();
            var shelf = 0;
            foreach (var group in groups)
            {
                var row = 0;
                foreach (var tile in (System.Collections.IEnumerable)group.GetType().GetProperty("Tiles")!.GetValue(group)!)
                {
                    var (s, r) = (shelf, row++);
                    tiles.Add(((string)tile.GetType().GetProperty("Title")!.GetValue(tile)!,
                        () => { tab.SetValue(top, s); cursor.SetValue(top, r); open.Invoke(top, null); }));
                }
                shelf++;
            }
            return tiles;
        }
        return [];
    }

    // Opens each item of the open menu or hub in turn, records where it led and any UI errors, and comes back with B
    // (a menu that shows itself again after an item counts as back), then with the reopen button if one is given.
    // Stops when it cannot come back.
    static string? Sweep(LiteRunner runner, ScriptedBackend backend, ulong? reopen, string[] skip)
    {
        var first = Choices();
        if (first.Count == 0)
            return "sweep: no menu or tool hub is open";
        var where = TopPadHandler()!.GetType().Name;
        var page = runner.CurrentPageName;
        var errors = new List<string>();
        void Count(Exception error, string at) => errors.Add($"{at}: {error.GetType().Name}: {error.Message}");
        UiErrors.Reported += Count;
        var problems = new List<string>();
        try
        {
            for (var i = 0; i < first.Count; i++)
            {
                var choices = Choices();
                if (TopPadHandler()?.GetType().Name != where || choices.Count != first.Count)
                {
                    problems.Add($"the {where} was not back before item {i + 1}");
                    break;
                }
                var label = choices[i].Label;
                if (label == "-" || skip.Any(name => Names(label, name)))
                    continue;
                var before = errors.Count;
                var loggedBefore = LoggedErrors().Count;
                choices[i].Choose();
                Frames(runner, backend, 1500);
                // A long job (legalizing, generating) shows a progress overlay until it is done: up to 30 s.
                for (var waited = 0; waited < 30 && TopPadHandler()?.GetType().Name == "PadOverlay"; waited++)
                    Frames(runner, backend, 1000);
                var reached = $"{runner.CurrentPageName}/{TopPadHandler()?.GetType().Name ?? "-"}";
                var back = false;
                bool Back() => TopPadHandler()?.GetType().Name == where && Choices().Count == first.Count;
                void Press(ulong button)
                {
                    backend.Held |= button;
                    runner.RunFrame();
                    backend.Held &= ~button;
                    Frames(runner, backend, 700);
                }
                // B until the menu is back, but never past the page the sweep started on.
                for (var presses = 0; presses < 4 && !(back = Back()) && (reopen is null || runner.CurrentPageName != page || TopPadHandler()?.GetType().Name != page); presses++)
                    Press(NpadButton.B);
                if (!back && reopen is { } button && runner.CurrentPageName == page)
                {
                    Press(button);
                    Frames(runner, backend, 800);
                    back = Back();
                    // An item that put the page in a mode of its own (select, a panel): B leaves the mode, then the menu opens.
                    if (!back)
                    {
                        Press(NpadButton.B);
                        if (runner.CurrentPageName == page)
                        {
                            Press(button);
                            Frames(runner, backend, 800);
                            back = Back();
                        }
                    }
                }
                var failed = errors.Skip(before).Concat(LoggedErrors().Skip(loggedBefore)).ToList();
                Console.WriteLine($"sweep {where} '{label}': {reached}{(failed.Count > 0 ? $", {failed.Count} UI error(s): {string.Join(" | ", failed)}" : "")}{(back ? "" : ", did not come back")}");
                if (failed.Count > 0)
                    problems.Add($"'{label}': {failed[0]}");
                if (!back)
                {
                    problems.Add($"'{label}' did not come back to the {where}");
                    break;
                }
            }
        }
        finally
        {
            UiErrors.Reported -= Count;
        }
        return problems.Count == 0 ? null : "sweep: " + string.Join("; ", problems);
    }

    // The ERROR lines of PKForge's own log (AppLog): a failure PKForge catches itself, such as a paint guard's blank
    // frame, never reaches UiErrors (the Poképark drew nothing that way, 2026-11-27).
    static List<string> LoggedErrors()
    {
        var log = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "logs", "pkforge.log");
        try
        {
            return File.Exists(log) ? [.. File.ReadLines(log).Where(entry => entry.Contains(" ERROR ", StringComparison.Ordinal))] : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    static PKForge.App.Services.IPadHandler? TopPadHandler()
    {
        var router = Microsoft.Maui.IPlatformApplication.Current?.Services.GetService(typeof(PKForge.App.Services.GamepadRouter));
        var stack = typeof(PKForge.App.Services.GamepadRouter).GetField("_stack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(router) as List<PKForge.App.Services.IPadHandler>;
        return stack is { Count: > 0 } ? stack[^1] : null;
    }

    static void Frames(LiteRunner runner, ScriptedBackend backend, double milliseconds)
    {
        var end = backend.Now + TimeSpan.FromMilliseconds(milliseconds);
        while (backend.Now < end)
            runner.RunFrame();
    }

    static void Tap(LiteRunner runner, ScriptedBackend backend, double x, double y)
    {
        backend.Touch(PointerAction.Pressed, x, y);
        runner.RunFrame();
        backend.Touch(PointerAction.Released, x, y);
        runner.RunFrame();
    }

    static void Drag(LiteRunner runner, ScriptedBackend backend, double x1, double y1, double x2, double y2, double milliseconds)
    {
        backend.Touch(PointerAction.Pressed, x1, y1);
        runner.RunFrame();
        var steps = Math.Max(1, (int)Math.Round(milliseconds / (1000.0 / 60)));
        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;
            backend.Touch(PointerAction.Moved, x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
            runner.RunFrame();
        }
        backend.Touch(PointerAction.Released, x2, y2);
        runner.RunFrame();
    }
}
