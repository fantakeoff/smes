using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SmesMobile.Core;
using SmesMobile.ViewModels;
using SmesMobile.Views;

namespace SmesMobile;

public partial class App : Application
{
    private readonly OfflineCommandStore _store;

    public App()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData)) localData = AppContext.BaseDirectory;
        var appDirectory = Path.Combine(localData, "SmesMobile");
        _store = new OfflineCommandStore(Path.Combine(appDirectory, "offline-commands.db3"));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    private MainViewModel CreateMainViewModel()
    {
#if DEBUG
        var api = new MobileApiClient(allowDevelopmentHttp: true);
#else
        var api = new MobileApiClient();
#endif
        return new MainViewModel(api, _store, new OfflineCommandProcessor(api, _store));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => new MainView { DataContext = CreateMainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            singleViewLifetime.MainView = new MainView { DataContext = CreateMainViewModel() };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
