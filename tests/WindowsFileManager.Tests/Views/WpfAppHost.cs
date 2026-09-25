using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace WindowsFileManager.Tests.Views;

/// <summary>
/// The one <see cref="App"/> the suite starts, and the one STA thread it lives on.
/// </summary>
/// <remarks>
/// <para>
/// WPF allows a single <see cref="System.Windows.Application"/> per AppDomain — a second <c>new App()</c> throws
/// even after the first one's thread has ended. Two test classes need the real application resources
/// (<see cref="XamlLoadTests"/> since T-002, the Broadsheet style runtime test since T-003), so they share this
/// host instead of each starting their own.
/// </para>
/// <para>
/// The bounds are unchanged from T-002: no window is shown and no message loop runs. Work items are executed
/// synchronously, in order, on the STA thread that constructed the <see cref="App"/>; <c>Application.Run()</c> is
/// never called. The thread is a background thread, so it never keeps the test process alive.
/// </para>
/// </remarks>
internal static class WpfAppHost
{
    private static readonly BlockingCollection<Action> Work = new();
    private static readonly Lazy<Thread> HostThread = new(Start);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private static ExceptionDispatchInfo? startupFailure;

    /// <summary>Runs <paramref name="action"/> on the host's STA thread, after the application has been initialised.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The work to run on the thread that owns the application.</param>
    /// <returns>What <paramref name="action"/> returned.</returns>
    public static T Run<T>(Func<T> action)
    {
        if (Thread.CurrentThread == HostThread.Value)
        {
            // Already on the host thread (a helper called from inside a work item): queuing would wait on
            // ourselves forever, so run inline.
            startupFailure?.Throw();
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try
            {
                startupFailure?.Throw();
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        // A hung WPF call fails the test instead of hanging the CI job.
        if (!completion.Task.Wait(Timeout))
        {
            throw new TimeoutException($"A WpfAppHost work item did not finish within {Timeout.TotalSeconds:0} s.");
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
            }
            catch (Exception ex)
            {
                startupFailure = ExceptionDispatchInfo.Capture(ex);
            }

            foreach (var item in Work.GetConsumingEnumerable())
            {
                item();
            }
        })
        {
            IsBackground = true,
            Name = "WpfAppHost STA",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
