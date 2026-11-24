using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>Aba "Conta": mostra o perfil, leva ao login/cadastro e permite sair.</summary>
public partial class PerfilViewModel : ObservableObject
{
    private readonly SupabaseService _supabase;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    public PerfilViewModel(SupabaseService supabase, IDialogService dialogos, INavigationService navegacao)
    {
        _supabase = supabase;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Deslogado))]
    private bool _logado;

    [ObservableProperty] private bool _precisaCadastro;
    [ObservableProperty] private bool _temCadastro;
    [ObservableProperty] private bool _ehBarbeiro;
    [ObservableProperty] private string _nome = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _telefone = string.Empty;
    [ObservableProperty] private string _inicial = "?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);
    public bool Deslogado => !Logado;

    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErroMensagem = null;
            await _supabase.InicializarAsync();

            Logado = _supabase.EstaLogado;
            if (!Logado)
            {
                Nome = Email = Telefone = string.Empty;
                PrecisaCadastro = TemCadastro = EhBarbeiro = false;
                return;
            }

            var cliente = await _supabase.ObterClienteAsync();
            TemCadastro = cliente is not null;
            PrecisaCadastro = cliente is null || !cliente.AceitoLgpd;

            Nome = cliente?.Nome ?? _supabase.NomeAtual ?? "Cliente";
            Email = cliente?.Email ?? _supabase.EmailAtual ?? string.Empty;
            Telefone = FormatarTelefone(cliente?.Telefone);
            Inicial = string.IsNullOrWhiteSpace(Nome) ? "?" : Nome.Trim()[..1].ToUpperInvariant();

            EhBarbeiro = await _supabase.EhBarbeiroAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Perfil] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar seu perfil.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task EntrarAsync() => _navegacao.GoToAsync("login");
    [RelayCommand] private Task EditarCadastroAsync() => _navegacao.GoToAsync("cadastro");

    [RelayCommand]
    private async Task SairAsync()
    {
        var ok = await _dialogos.ConfirmAsync("Sair", "Deseja sair da sua conta?", "Sair", "Ficar");
        if (!ok) return;

        try
        {
            await _supabase.SairAsync();
            await CarregarAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Perfil] Falha ao sair: {ex}");
            await _dialogos.AlertAsync("Ops", "Não foi possível sair. Tente novamente.");
        }
    }

    private static string FormatarTelefone(string? t)
    {
        var d = new string((t ?? string.Empty).Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            11 => $"({d[..2]}) {d.Substring(2, 5)}-{d[7..]}",
            10 => $"({d[..2]}) {d.Substring(2, 4)}-{d[6..]}",
            _ => t ?? string.Empty
        };
    }
}
