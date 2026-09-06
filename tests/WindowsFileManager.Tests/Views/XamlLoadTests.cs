using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WindowsFileManager.Views.Chrome;
using WindowsFileManager.Views.Panels;
using WindowsFileManager.Views.Screens;

namespace WindowsFileManager.Tests.Views;

/// <summary>
/// Regression net for the T-002 decomposition.
/// </summary>
/// <remarks>
/// <para>
/// Splitting MainWindow.xaml into nine UserControls creates exactly one new failure
/// mode: a <c>{StaticResource}</c> that used to resolve against the Window's own
/// <c>Resources</c> block now has to resolve against <c>Application.Resources</c>
/// instead. When it doesn't, the XamlParseException is thrown while the control is
/// parsed — which, for a panel that ships <c>Collapsed</c>, means it surfaces the
/// first time a user opens that panel and never before. A manual checklist cannot
/// catch that; this can.
/// </para>
/// <para>
/// This is therefore the only place in the suite that starts an
/// <see cref="Application"/> — a deliberate, scoped exception to the "never start an
/// Application or open a window" rule in docs/modules/ui.md § Testing. No window is
/// shown and no message loop is run: all nine controls are built on one private STA
/// thread this fixture owns, then dropped. See T-002 fork 2.
/// </para>
/// </remarks>
public class XamlLoadTests
{
    /// <summary>
    /// Every control the decomposition produced, paired with its constructor. Kept in
    /// step with the shell by <see cref="TheShell_ComposesEveryDecomposedControl"/>, so a
    /// tenth control cannot be added without also being parse-tested.
    /// </summary>
    private static readonly (string Name, Func<UserControl> Create)[] Decomposed = new (string, Func<UserControl>)[]
    {
        (nameof(ProfileBar), () => new ProfileBar()),
        (nameof(AppStatusBar), () => new AppStatusBar()),
        (nameof(ScanScopeBar), () => new ScanScopeBar()),
        (nameof(FoldersScreen), () => new FoldersScreen()),
        (nameof(DuplicatesScreen), () => new DuplicatesScreen()),
        (nameof(HistoryScreen), () => new HistoryScreen()),
        (nameof(AnalyticsPanel), () => new AnalyticsPanel()),
        (nameof(FolderActionPanel), () => new FolderActionPanel()),
        (nameof(PreviewPanel), () => new PreviewPanel()),
    };

    private static string RepositoryRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "WindowsFileManager.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new InvalidOperationException("WindowsFileManager.sln not found above the test binaries.");
        }
    }

    /// <summary>
    /// All nine are built on a single STA thread — one thread, so the controls are always
    /// constructed on the same thread that owns the <see cref="Application"/> whose
    /// resources they resolve against. Every failure is collected before asserting, so one
    /// run names every broken control rather than only the first.
    /// </summary>
    [Fact]
    public void EveryDecomposedControl_ParsesWithoutError()
    {
        var failures = RunOnStaThread(() =>
        {
            var app = new App();
            app.InitializeComponent();

            var errors = new List<string>();
            foreach (var (name, create) in Decomposed)
            {
                try
                {
                    create();
                }
                catch (Exception ex)
                {
                    // The inner exception carries the unresolved key; the outer one only
                    // says "provide value on ... threw an exception", which is useless.
                    errors.Add($"{name}: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            return errors;
        });

        Assert.Empty(failures);
    }

    /// <summary>
    /// The shell must compose exactly the nine listed above. A control that parses but is
    /// no longer referenced is dead code; a control added to the shell without a row in
    /// <see cref="Decomposed"/> would otherwise never be parse-tested.
    /// </summary>
    [Fact]
    public void TheShell_ComposesEveryDecomposedControl()
    {
        var shellPath = Path.Combine(RepositoryRoot, "src", "WindowsFileManager", "Views", "MainWindow.xaml");
        var composed = new Regex(@"<(?:chrome|screens|panels):([A-Za-z0-9_]+)\b", RegexOptions.Compiled)
            .Matches(File.ReadAllText(shellPath))
            .Select(m => m.Groups[1].Value)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var listed = Decomposed.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(listed, composed);
    }

    private static List<string> RunOnStaThread(Func<List<string>> action)
    {
        var result = new List<string>();
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("The STA test thread threw before it could report.", failure);
        }

        return result;
    }
}
