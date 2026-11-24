namespace LumosBarbearia;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent(); // 1. Carrega os recursos (LumosBg, etc) PRIMEIRO
        UserAppTheme = AppTheme.Dark;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // 2. Só cria o AppShell DEPOIS que os recursos já estão na memória
        var shell = _services.GetRequiredService<AppShell>();
        return new Window(shell) { Title = "Lumos Barbearia" };
    }
}