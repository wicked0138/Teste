using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Models;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>Um agendamento na lista do Balcão. O Status é observável para a linha reagir às ações.</summary>
public partial class AgendaItem : ObservableObject
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public AgendaItem(AgendaDetalhada dados, ICommand marcar, ICommand desmarcar)
    {
        Id = dados.Id;
        Cliente = dados.Cliente;
        Telefone = dados.TelefoneCliente;
        Servico = dados.Servico;
        Barbeiro = dados.Barbeiro;
        Preco = dados.Preco;
        Inicio = dados.HorarioTs;
        Fim = dados.FimTs;
        _status = dados.Status;
        Marcar = marcar;
        Desmarcar = desmarcar;
    }

    public long Id { get; }
    public string Cliente { get; }
    public string Telefone { get; }
    public string Servico { get; }
    public string Barbeiro { get; }
    public decimal Preco { get; }
    public TimeSpan Inicio { get; }
    public TimeSpan Fim { get; }

    public ICommand Marcar { get; }
    public ICommand Desmarcar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTexto))]
    [NotifyPropertyChangedFor(nameof(PodeMarcar))]
    [NotifyPropertyChangedFor(nameof(PodeDesmarcar))]
    [NotifyPropertyChangedFor(nameof(IsCancelado))]
    private string _status;

    public string HorarioTexto => Inicio.ToString(@"hh\:mm");
    public string FimTexto => "até " + Fim.ToString(@"hh\:mm");
    public string Detalhe => $"{Servico} · {Barbeiro}";
    public string PrecoTexto => Preco.ToString("C", PtBr);
    public double Minutos => (Fim - Inicio).TotalMinutes;

    public string StatusTexto => Status switch
    {
        StatusAgendamento.Confirmado => "Confirmado",
        StatusAgendamento.Concluido => "Concluído",
        StatusAgendamento.Cancelado => "Cancelado",
        _ => "A Confirmar"
    };

    public bool PodeMarcar => Status == StatusAgendamento.Pendente;
    public bool PodeDesmarcar => Status is StatusAgendamento.Pendente or StatusAgendamento.Confirmado;
    public bool IsCancelado => Status == StatusAgendamento.Cancelado;
}

/// <summary>
/// ViewModel da aba "Balcão" (só barbeiros): ocupação, previsão de faturamento e agenda do dia,
/// com confirmar ("Marcar") e cancelar ("Desmarcar"). Atualiza sozinho via Realtime.
/// A segurança real está no RLS do banco; aqui o acesso negado é só experiência de uso.
/// </summary>
public partial class BalcaoViewModel : ObservableObject
{
    // Mesmas regras de funcionamento do AgendarViewModel / trigger do banco (08:00–20:00)
    private static readonly double MinutosPorBarbeiro = (TimeSpan.FromHours(20) - TimeSpan.FromHours(8)).TotalMinutes;

    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly SupabaseService _supabase;
    private readonly AgendamentoService _agendamentos;
    private readonly BalcaoService _balcao;
    private readonly AgendamentosRealtimeService _realtime;
    private readonly IDialogService _dialogos;

    private int _qtdBarbeiros;
    private int _versaoCarga;
    private CancellationTokenSource? _debounce;

    public BalcaoViewModel(
        SupabaseService supabase,
        AgendamentoService agendamentos,
        BalcaoService balcao,
        AgendamentosRealtimeService realtime,
        IDialogService dialogos)
    {
        _supabase = supabase;
        _agendamentos = agendamentos;
        _balcao = balcao;
        _realtime = realtime;
        _dialogos = dialogos;

        _dataSelecionada = Hoje();
    }

    public ObservableCollection<AgendaItem> Agenda { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataTexto))]
    private DateTime _dataSelecionada;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeVerAgenda))]
    private bool _acessoNegado;
    [ObservableProperty] private bool _semAgendamentos;
    [ObservableProperty] private string _ocupacaoTexto = "0%";
    [ObservableProperty] private string _ocupacaoDetalhe = "0 atendimentos";
    [ObservableProperty] private string _faturamentoTexto = 0m.ToString("C", PtBr);
    [ObservableProperty] private string _faturamentoDetalhe = "previsto para o dia";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);

    /// <summary>Inverso de AcessoNegado (evita conversor na XAML).</summary>
    public bool PodeVerAgenda => !AcessoNegado;

    public string DataTexto
    {
        get
        {
            var dia = DataSelecionada.ToString("dddd, dd/MM", PtBr);
            dia = char.ToUpper(dia[0], PtBr) + dia[1..];
            return DataSelecionada == Hoje() ? $"Hoje · {dia}" : dia;
        }
    }

    // =====================================================================
    // CICLO DE VIDA (chamado pela página)
    // =====================================================================

    public async Task AtivarAsync()
    {
        _realtime.AgendamentoAlterado -= OnAgendamentoAlterado; // evita assinar duas vezes
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

            // Só barbeiros: UI amigável (o RLS é a barreira real)
            AcessoNegado = !_supabase.EstaLogado || !await _supabase.EhBarbeiroAsync();
            if (AcessoNegado)
            {
                Agenda.Clear();
                return;
            }

            if (_qtdBarbeiros == 0)
                _qtdBarbeiros = (await _agendamentos.ObterBarbeirosAsync()).Count;

            await IniciarRealtimeSeguroAsync();
            await RecarregarAgendaAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Balcão] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar a agenda. Verifique sua conexão.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pull-to-refresh.</summary>
    [RelayCommand]
    private async Task AtualizarAsync()
    {
        try
        {
            await RecarregarAgendaAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Balcão] Falha ao atualizar: {ex}");
            ErroMensagem = "Não foi possível atualizar a agenda.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // =====================================================================
    // NAVEGAÇÃO ENTRE DIAS
    // =====================================================================

    [RelayCommand]
    private Task DiaAnteriorAsync() => TrocarDiaAsync(DataSelecionada.AddDays(-1));

    [RelayCommand]
    private Task ProximoDiaAsync() => TrocarDiaAsync(DataSelecionada.AddDays(1));

    [RelayCommand]
    private Task IrParaHojeAsync() => TrocarDiaAsync(Hoje());

    private async Task TrocarDiaAsync(DateTime dia)
    {
        DataSelecionada = dia;
        try
        {
            ErroMensagem = null;
            await RecarregarAgendaAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Balcão] Falha ao trocar de dia: {ex}");
            ErroMensagem = "Não foi possível carregar este dia.";
        }
    }

    // =====================================================================
    // AÇÕES NOS AGENDAMENTOS
    // =====================================================================

    /// <summary>"Marcar": confirma o agendamento (cliente passa a ver "Confirmado").</summary>
    [RelayCommand]
    private async Task MarcarAsync(AgendaItem? item)
    {
        if (item is null || !item.PodeMarcar) return;
        await AlterarStatusAsync(item, StatusAgendamento.Confirmado);
    }

    /// <summary>"Desmarcar": cancela o agendamento (libera o horário na grade dos clientes).</summary>
    [RelayCommand]
    private async Task DesmarcarAsync(AgendaItem? item)
    {
        if (item is null || !item.PodeDesmarcar) return;

        var ok = await _dialogos.ConfirmAsync(
            "Desmarcar atendimento",
            $"Cancelar o horário das {item.HorarioTexto} de {item.Cliente}?");
        if (!ok) return;

        await AlterarStatusAsync(item, StatusAgendamento.Cancelado);
    }

    private async Task AlterarStatusAsync(AgendaItem item, string novoStatus)
    {
        try
        {
            var alterou = await _balcao.AtualizarStatusAsync(item.Id, novoStatus);
            if (!alterou)
            {
                await _dialogos.AlertAsync("Sem permissão", "Não foi possível alterar este agendamento.");
                return;
            }

            item.Status = novoStatus; // atualização imediata da linha (o Realtime confirma em seguida)
            CalcularResumo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Balcão] Falha ao alterar status: {ex}");
            await _dialogos.AlertAsync("Ops", "Não foi possível atualizar o agendamento. Tente novamente.");
        }
    }

    // =====================================================================
    // DADOS E REALTIME
    // =====================================================================

    private async Task RecarregarAgendaAsync()
    {
        var versao = ++_versaoCarga;
        var dados = await _balcao.ObterAgendaAsync(DataSelecionada);
        if (versao != _versaoCarga) return; // resposta antiga

        Agenda.Clear();
        foreach (var d in dados)
            Agenda.Add(new AgendaItem(d, MarcarCommand, DesmarcarCommand));

        SemAgendamentos = Agenda.Count == 0;
        CalcularResumo();
    }

    /// <summary>Cards do topo. Cancelados não contam. Faturamento aqui é PREVISÃO (inclui pendentes).</summary>
    private void CalcularResumo()
    {
        var ativos = Agenda.Where(a => !a.IsCancelado).ToList();

        var capacidade = MinutosPorBarbeiro * Math.Max(1, _qtdBarbeiros);
        var ocupacao = capacidade > 0 ? ativos.Sum(a => a.Minutos) / capacidade : 0;

        OcupacaoTexto = $"{Math.Round(ocupacao * 100):0}%";
        OcupacaoDetalhe = ativos.Count == 1 ? "1 atendimento" : $"{ativos.Count} atendimentos";

        FaturamentoTexto = ativos.Sum(a => a.Preco).ToString("C", PtBr);
        FaturamentoDetalhe = $"{ativos.Count(a => a.Status != StatusAgendamento.Pendente)} confirmados · " +
                             $"{ativos.Count(a => a.Status == StatusAgendamento.Pendente)} a confirmar";
    }

    private async Task IniciarRealtimeSeguroAsync()
    {
        try
        {
            await _realtime.IniciarAsync();
        }
        catch (Exception ex)
        {
            // Sem Realtime o Balcão continua funcionando (pull-to-refresh)
            System.Diagnostics.Debug.WriteLine($"[Balcão] Realtime indisponível: {ex.Message}");
        }
    }

    /// <summary>Evento do Realtime (thread de fundo): agrupa rajadas e recarrega a agenda do dia.</summary>
    private void OnAgendamentoAlterado(object? sender, EventArgs e)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                await RecarregarAgendaAsync();
            }
            catch (TaskCanceledException) { /* chegou outro evento; este foi descartado */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Balcão] Falha ao recarregar via Realtime: {ex.Message}");
            }
        });
    }

    private static DateTime Hoje() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).Date;
}
