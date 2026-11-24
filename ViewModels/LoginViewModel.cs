using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>Entrar com Google (Android) ou com e-mail e senha (Android e Windows) / criar conta.</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly SupabaseService _supabase;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    public LoginViewModel(SupabaseService supabase, IDialogService dialogos, INavigationService navegacao)
    {
        _supabase = supabase;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _senha = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);

    /// <summary>O WebAuthenticator (login Google) não é suportado no Windows.</summary>
    public bool GoogleDisponivel => DeviceInfo.Platform != DevicePlatform.WinUI;

    [RelayCommand]
    private async Task EntrarGoogleAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErroMensagem = null;

            if (await _supabase.EntrarComGoogleAsync())
                await ConcluirAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Google: {ex}");
            ErroMensagem = "Não foi possível entrar com o Google. Tente novamente ou use e-mail e senha.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task EntrarEmailAsync()
    {
        if (IsBusy || !Validar(out var email)) return;
        try
        {
            IsBusy = true;
            ErroMensagem = null;

            var erro = await _supabase.EntrarComEmailAsync(email, Senha);
            if (erro is not null) { ErroMensagem = erro; return; }
            await ConcluirAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] E-mail: {ex}");
            ErroMensagem = "Não foi possível entrar. Verifique sua conexão.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CriarContaAsync()
    {
        if (IsBusy || !Validar(out var email)) return;
        try
        {
            IsBusy = true;
            ErroMensagem = null;

            var (ok, mensagem) = await _supabase.CriarContaAsync(email, Senha);
            if (!ok) { ErroMensagem = mensagem; return; }

            if (_supabase.EstaLogado) { await ConcluirAsync(); return; }
            await _dialogos.AlertAsync("Quase lá", mensagem);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Login] Criar conta: {ex}");
            ErroMensagem = "Não foi possível criar a conta. Verifique sua conexão.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task FecharAsync() => _navegacao.GoToAsync("..");

    private Task ConcluirAsync()
    {
        Senha = string.Empty;
        return _navegacao.GoToAsync("..");
    }

    private bool Validar(out string email)
    {
        email = Email?.Trim() ?? string.Empty;
        if (!email.Contains('@') || !email.Contains('.')) { ErroMensagem = "Informe um e-mail válido."; return false; }
        if ((Senha ?? string.Empty).Length < 6) { ErroMensagem = "A senha precisa ter pelo menos 6 caracteres."; return false; }
        return true;
    }
}
