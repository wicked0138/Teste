using LumosBarbearia.Services;
using LumosBarbearia.Views;

namespace LumosBarbearia;

public partial class AppShell : Shell
{
    private readonly SupabaseService _supabase;

    public AppShell(SupabaseService supabase)
    {
        InitializeComponent();
        _supabase = supabase;

        // Páginas abertas por cima das abas (GoToAsync("login"), etc.)
        Routing.RegisterRoute("login", typeof(LoginPage));
        Routing.RegisterRoute("cadastro", typeof(CadastroPage));
        Routing.RegisterRoute("remarcar", typeof(RemarcarPage));

        // A aba Balcão aparece/some conforme o login muda
        _supabase.AutenticacaoAlterada += async (_, _) => await AtualizarAbaBalcaoAsync();
        Loaded += async (_, _) => await AtualizarAbaBalcaoAsync();
    }

    private async Task AtualizarAbaBalcaoAsync()
    {
        try
        {
            await _supabase.InicializarAsync();
            var barbeiro = _supabase.EstaLogado && await _supabase.EhBarbeiroAsync();
            await MainThread.InvokeOnMainThreadAsync(() => BalcaoTab.IsVisible = barbeiro);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shell] Falha ao checar barbeiro: {ex.Message}");
        }
    }
}
