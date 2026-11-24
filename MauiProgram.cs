using LumosBarbearia.Config;
using LumosBarbearia.Services;
using LumosBarbearia.ViewModels;
using LumosBarbearia.Views;
using Microsoft.Extensions.Logging;
using Supabase;

namespace LumosBarbearia;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        if (!SupabaseConfig.EstaConfigurado)
            throw new InvalidOperationException(
                "Configure a URL e a chave pública do Supabase em Config/SupabaseConfig.cs.");

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        // ---------- Supabase (um único cliente para o app inteiro) ----------
        builder.Services.AddSingleton(_ => new Supabase.Client(
            SupabaseConfig.Url,
            SupabaseConfig.ChavePublica,
            new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true,
                SessionHandler = new SupabaseSessionHandler()
            }));

        // ---------- Services ----------
        builder.Services.AddSingleton<SupabaseService>();
        builder.Services.AddSingleton<AgendamentosRealtimeService>();
        builder.Services.AddSingleton<AgendamentoService>();
        builder.Services.AddSingleton<ClienteService>();
        builder.Services.AddSingleton<BalcaoService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        // ---------- ViewModels e Views ----------
        builder.Services.AddTransient<AgendarViewModel>();
        builder.Services.AddTransient<AgendarPage>();
        builder.Services.AddTransient<MeusHorariosViewModel>();
        builder.Services.AddTransient<MeusHorariosPage>();
        builder.Services.AddTransient<BalcaoViewModel>();
        builder.Services.AddTransient<BalcaoPage>();
        builder.Services.AddTransient<PerfilViewModel>();
        builder.Services.AddTransient<PerfilPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<CadastroViewModel>();
        builder.Services.AddTransient<CadastroPage>();
        builder.Services.AddTransient<RemarcarViewModel>();
        builder.Services.AddTransient<RemarcarPage>();

        builder.Services.AddSingleton<AppShell>();

        // Garante que o App seja criado com a injeção de dependência correta
        builder.Services.AddSingleton<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}