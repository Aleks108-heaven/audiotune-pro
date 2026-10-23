using System.Windows;
using System.Windows.Threading;

namespace AudioTunePro.App.UiTests;

/// <summary>
/// One WPF <see cref="Application"/> and one STA dispatcher thread shared by every UI test. WPF allows a single
/// Application per process and its objects belong to the thread that created them, so each test body runs through
/// <see cref="Run{T}"/>. The application carries the app's real design tokens and styles, loaded by pack URI from the
/// app assembly exactly as App.xaml does.
/// </summary>
public sealed class WpfFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public WpfFixture()
    {
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(Load("Views/AudioTunePro.Tokens.xaml"));
                app.Resources.MergedDictionaries.Add(Load("Views/Styles.xaml"));
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (failure is null) Dispatcher.Run();
        })
        {
            Name = "WPF UI tests",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        if (failure is not null || dispatcher is null)
            throw new InvalidOperationException("The WPF test application could not be started.", failure);
        _dispatcher = dispatcher;
    }

    private static ResourceDictionary Load(string path) =>
        new() { Source = new Uri($"pack://application:,,,/AudioTunePro;component/{path}") };

    /// <summary>Runs <paramref name="action"/> on the UI thread; an exception inside it is rethrown to the caller.</summary>
    public T Run<T>(Func<T> action) => _dispatcher.Invoke(action);

    public void Run(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}

/// <summary>Puts every UI test in one collection: they share the single WPF application and must not run in parallel.</summary>
[CollectionDefinition(Name)]
public sealed class WpfCollection : ICollectionFixture<WpfFixture>
{
    public const string Name = "WPF";
}
