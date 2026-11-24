using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Models;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>Um agendamento do cliente. O Status é observável: muda na tela quando o barbeiro confirma (Realtime).</summary>
public partial class MeuAgendamentoItem : ObservableObject
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public MeuAgendamentoItem(AgendaDetalhada dados, bool passado, ICommand cancelar, ICommand remarcar)
    {
        Dados = dados;
        IsPassado = passado;
        Cancelar = cancelar;
        Remarcar = remarcar;
        _status = dados.Status;
    }

    public AgendaDetalhada Dados { get; }
    public bool IsPassado { get; }
    public ICommand Cancelar { get; }
    public ICommand Remarcar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTexto))]
    [NotifyPropertyChangedFor(nameof(PodeAlterar))]
    private string _status;

    public long Id => Dados.Id;
    public string Servico => Dados.Servico;
    public string Barbeiro => $"com {Dados.Barbeiro}";
    public string PrecoTexto => Dados.Preco.ToString("C", PtBr);
    public string HorarioTexto => $"{Dados.HorarioTs:hh\\:mm} – {Dados.FimTs:hh\\:mm}";

    public string DataTexto
    {
        get
        {
            var t = Dados.Data.ToString("dddd, dd 'de' MMMM", PtBr);
            return char.ToUpper(t[0], PtBr) + t[1..];
        }
    }

    public string StatusTexto => Status switch
    {
        StatusAgendamento.Confirmado => "Confirmado",
        StatusAgendamento.Concluido => "Concluído",
        StatusAgendamento.Cancelado => "Cancelado",
        _ => "A Confirmar"
    };

    /// <summary>Só dá para cancelar/remarcar o que ainda vai acontecer e não foi cancelado/concluído.</summary>
    public bool PodeAlterar => !IsPassado && Status is StatusAgendamento.Pendente or StatusAgendamento.Confirmado;
}

/// <summary>Aba "Meus horários": lista do cliente, com status em tempo real, cancelar e remarcar.</summary>
public partial class MeusHorariosViewModel : ObservableObject
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly SupabaseService _supabase;
    private readonly AgendamentoService _agendamentos;
    private readonly AgendamentosRealtimeService _realtime;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    private int _versaoCarga;
    private CancellationTokenSource? _debounce;

    public MeusHorariosViewModel(
        SupabaseService supabase,
        AgendamentoService agendamentos,
        AgendamentosRealtimeService realtime,
        IDialogService dialogos,
        INavigationService navegacao)
    {
        _supabase = supabase;
        _agendamentos = agendamentos;
        _realtime = realtime;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    public ObservableCollection<MeuAgendamentoItem> Itens { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarLista))]
    [NotifyPropertyChangedFor(nameof(PrecisaLogin))]
    private bool _logado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarLista))]
    private bool _precisaCadastro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);
    public bool PrecisaLogin => !Logado;
    public bool MostrarLista => Logado && !PrecisaCadastro;

    // ---------- Ciclo de vida ----------

    public async Task AtivarAsync()
    {
        _realtime.AgendamentoAlterado -= OnAgendamentoAlterado;
        _realtime.AgendamentoAlterado += OnAgendamentoAlterado;
        await CarregarCommand.ExecuteAsync(null);
    }

    public void Desativar()
    {
        _realtime.AgendamentoAlterado -= OnAgendamentoAlterado;
        _realtime.Parar();
    }

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
                Itens.Clear();
                return;
            }

            try { await _realtime.IniciarAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meus] Realtime indisponível: {ex.Message}"); }

            await RecarregarAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Meus] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar seus horários. Verifique sua conexão.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AtualizarAsync()
    {
        try { await RecarregarAsync(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Meus] Falha ao atualizar: {ex}");
            ErroMensagem = "Não foi possível atualizar.";
        }
        finally { IsRefreshing = false; }
    }

    // ---------- Navegação ----------

    [RelayCommand] private Task IrLoginAsync() => _navegacao.GoToAsync("login");
    [RelayCommand] private Task IrCadastroAsync() => _navegacao.GoToAsync("cadastro");
    [RelayCommand] private Task NovoAgendamentoAsync() => _navegacao.GoToAsync("//agendar");

    // ---------- Ações ----------

    [RelayCommand]
    private async Task CancelarAsync(MeuAgendamentoItem? item)
    {
        if (item is null || !item.PodeAlterar) return;

        var ok = await _dialogos.ConfirmAsync("Cancelar horário",
            $"Cancelar {item.Servico} em {item.DataTexto}, {item.HorarioTexto}?", "Cancelar horário", "Voltar");
        if (!ok) return;

        try
        {
            var r = await _agendamentos.CancelarAsync(item.Id);
            if (!r.Sucesso) { await _dialogos.AlertAsync("Ops", r.Mensagem); return; }

            item.Status = StatusAgendamento.Cancelado;
            await RecarregarAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Meus] Falha ao cancelar: {ex}");
            await _dialogos.AlertAsync("Ops", "Não foi possível cancelar. Tente novamente.");
        }
    }

    [RelayCommand]
    private async Task RemarcarAsync(MeuAgendamentoItem? item)
    {
        if (item is null || !item.PodeAlterar) return;
        await _navegacao.GoToAsync("remarcar", new Dictionary<string, object> { ["agendamento"] = item.Dados });
    }

    // ---------- Dados / Realtime ----------

    private async Task RecarregarAsync()
    {
        var versao = ++_versaoCarga;

        var cliente = await _supabase.ObterClienteAsync();
        if (versao != _versaoCarga) return;

        PrecisaCadastro = cliente is null || !cliente.AceitoLgpd;
        if (cliente is null)
        {
            Itens.Clear();
            return;
        }

        var dados = await _agendamentos.ObterDoClienteAsync(cliente.Id);
        if (versao != _versaoCarga) return;

        var agora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime;
        bool EhFuturo(AgendaDetalhada a) => a.Data.Date + a.FimTs > agora;
        bool EhAtivo(AgendaDetalhada a) => a.Status is StatusAgendamento.Pendente or StatusAgendamento.Confirmado;

        // Próximos (ativos e futuros) em ordem crescente; depois o histórico, do mais recente ao mais antigo
        var proximos = dados.Where(a => EhAtivo(a) && EhFuturo(a)).OrderBy(a => a.Data).ThenBy(a => a.HorarioTs);
        var historico = dados.Where(a => !(EhAtivo(a) && EhFuturo(a))).OrderByDescending(a => a.Data).ThenByDescending(a => a.HorarioTs);

        Itens.Clear();
        foreach (var a in proximos) Itens.Add(new MeuAgendamentoItem(a, false, CancelarCommand, RemarcarCommand));
        foreach (var a in historico) Itens.Add(new MeuAgendamentoItem(a, true, CancelarCommand, RemarcarCommand));
    }

    /// <summary>Evento do Realtime (thread de fundo): agrupa rajadas e recarrega a lista.</summary>
    private void OnAgendamentoAlterado(object? sender, EventArgs e)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                await RecarregarAsync();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meus] Realtime: {ex.Message}"); }
        });
    }
}
