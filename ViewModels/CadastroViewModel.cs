using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>
/// Conclui o cadastro do cliente logado: nome, e-mail, telefone e aceite da LGPD.
/// Necessário porque o login (ex.: Google) só cria a conta de autenticação, não a linha em "clientes".
/// </summary>
public partial class CadastroViewModel : ObservableObject
{
    private readonly SupabaseService _supabase;
    private readonly ClienteService _clientes;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    public CadastroViewModel(
        SupabaseService supabase,
        ClienteService clientes,
        IDialogService dialogos,
        INavigationService navegacao)
    {
        _supabase = supabase;
        _clientes = clientes;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    [ObservableProperty] private string _nome = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _telefone = string.Empty;
    [ObservableProperty] private bool _aceitouLgpd;
    [ObservableProperty] private bool _isBusy;

    /// <summary>E-mail vindo da conta de login não deve ser editado (precisa coincidir com a conta).</summary>
    [ObservableProperty] private bool _emailEditavel = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);

    /// <summary>Texto exibido ao lado do aceite. Se alterar, atualize ClienteService.VersaoTermo.</summary>
    public string TextoLgpd =>
        "Autorizo a Lumos Barbearia a tratar meu nome, e-mail e telefone para criar minha conta, " +
        "gerenciar meus agendamentos e enviar avisos sobre eles, conforme a Lei Geral de Proteção de Dados " +
        "(Lei 13.709/2018). Posso revogar este consentimento a qualquer momento.";

    // =====================================================================

    /// <summary>Pré-preenche com os dados da conta (Google) ou do cadastro já existente.</summary>
    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (!_supabase.EstaLogado)
        {
            await _navegacao.GoToAsync("login");
            return;
        }

        try
        {
            IsBusy = true;
            var existente = await _supabase.ObterClienteAsync();

            if (existente is not null)
            {
                Nome = existente.Nome;
                Email = existente.Email;
                Telefone = existente.Telefone;
            }
            else
            {
                Nome = _supabase.NomeAtual ?? string.Empty;
                Email = _supabase.EmailAtual ?? string.Empty;
            }

            EmailEditavel = string.IsNullOrEmpty(_supabase.EmailAtual);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cadastro] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar seus dados.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        if (IsBusy) return;

        var nome = Nome?.Trim() ?? string.Empty;
        var email = Email?.Trim() ?? string.Empty;
        var telefone = SomenteDigitos(Telefone);

        // Validações espelham as constraints do banco (ck_cli_email, ck_cli_telefone, ck_cli_lgpd)
        if (nome.Length < 3) { ErroMensagem = "Informe seu nome completo."; return; }
        if (!EmailValido(email)) { ErroMensagem = "Informe um e-mail válido."; return; }
        if (telefone.Length is < 10 or > 11) { ErroMensagem = "Telefone inválido. Use DDD + número."; return; }
        if (!AceitouLgpd) { ErroMensagem = "Para continuar, aceite o tratamento dos dados (LGPD)."; return; }

        try
        {
            IsBusy = true;
            ErroMensagem = null;

            var resultado = await _clientes.CadastrarAsync(nome, email, telefone);
            if (!resultado.Sucesso)
            {
                ErroMensagem = resultado.Mensagem;
                return;
            }

            await _dialogos.AlertAsync("Tudo certo!", "Seu cadastro foi concluído. Agora é só reservar.");
            await _navegacao.GoToAsync(".."); // volta para a tela de onde veio (Agendar)
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cadastro] Falha ao salvar: {ex}");
            ErroMensagem = "Não foi possível salvar seu cadastro. Tente novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------- Auxiliares ----------

    /// <summary>Remove máscara e o DDI 55 (+55 11 91234-5678 → 11912345678).</summary>
    private static string SomenteDigitos(string? valor)
    {
        var digitos = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.Length is 12 or 13 && digitos.StartsWith("55"))
            digitos = digitos[2..];
        return digitos;
    }

    private static bool EmailValido(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            var m = new MailAddress(email);
            return m.Address == email && email.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
