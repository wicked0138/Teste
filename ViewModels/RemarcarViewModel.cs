using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Models;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>
/// Remarcar: mantém barbeiro e serviço do agendamento e permite trocar dia e horário.
/// Recebe o agendamento por parâmetro de navegação ("agendamento"). O horário atual do próprio
/// agendamento é tratado como livre, para poder deslocá-lo em poucos minutos.
/// </summary>
public partial class RemarcarViewModel : ObservableObject, IQueryAttributable
{
    // Regras de funcionamento (iguais ao AgendarViewModel e ao trigger do banco)
    private const int DiasVisiveis = 14;
    private const int IntervaloMinutos = 30;
    private static readonly TimeSpan Abertura = TimeSpan.FromHours(8);
    private static readonly TimeSpan Fechamento = TimeSpan.FromHours(20);
    private static readonly DayOfWeek DiaFechado = DayOfWeek.Sunday;

    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly AgendamentoService _agendamentos;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    private AgendaDetalhada? _original;
    private List<HorarioOcupado> _ocupados = new();
    private int _versaoCarga;

    public RemarcarViewModel(AgendamentoService agendamentos, IDialogService dialogos, INavigationService navegacao)
    {
        _agendamentos = agendamentos;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    public ObservableCollection<DiaItem> Dias { get; } = new();
    public ObservableCollection<HorarioItem> Horarios { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeConfirmar))]
    private bool _isBusy;

    [ObservableProperty] private bool _semHorarios;
    [ObservableProperty] private string _titulo = "Remarcar";
    [ObservableProperty] private string _detalhe = string.Empty;
    [ObservableProperty] private string _atual = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    [ObservableProperty] private DiaItem? _diaSelecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeConfirmar))]
    private HorarioItem? _horarioSelecionado;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);
    public bool PodeConfirmar => HorarioSelecionado is not null && !IsBusy;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("agendamento", out var o) && o is AgendaDetalhada a)
        {
            _original = a;
            Detalhe = $"{a.Servico} com {a.Barbeiro}";
            Atual = $"Atual: {a.Data:dd/MM} às {a.HorarioTs:hh\\:mm}";
            if (Dias.Count > 0) { Dias.Clear(); Horarios.Clear(); }
        }
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (_original is null || IsBusy) return;
        try
        {
            IsBusy = true;
            ErroMensagem = null;
            if (Dias.Count == 0) GerarDias();
            await CarregarHorariosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remarcar] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar os horários.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelecionarDiaAsync(DiaItem? dia)
    {
        if (dia is null || dia == DiaSelecionado) return;
        MarcarDia(dia);
        try
        {
            ErroMensagem = null;
            await CarregarHorariosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remarcar] Falha ao trocar de dia: {ex}");
            ErroMensagem = "Não foi possível atualizar os horários.";
        }
    }

    [RelayCommand]
    private void SelecionarHorario(HorarioItem? horario)
    {
        if (horario is null || !horario.Disponivel) return;
        if (HorarioSelecionado is not null) HorarioSelecionado.IsSelected = false;
        horario.IsSelected = true;
        HorarioSelecionado = horario;
    }

    [RelayCommand]
    private async Task ConfirmarAsync()
    {
        if (_original is null || DiaSelecionado is null || HorarioSelecionado is null || IsBusy) return;
        try
        {
            IsBusy = true;
            var r = await _agendamentos.RemarcarAsync(_original.Id, DiaSelecionado.Data, HorarioSelecionado.Inicio);
            if (!r.Sucesso)
            {
                await _dialogos.AlertAsync("Ops", r.Mensagem);
                await CarregarHorariosAsync();
                return;
            }

            await _dialogos.AlertAsync("Remarcado!", r.Mensagem);
            await _navegacao.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remarcar] Falha: {ex}");
            await _dialogos.AlertAsync("Ops", "Não foi possível remarcar. Tente novamente.");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task VoltarAsync() => _navegacao.GoToAsync("..");

    // ---------- Interno ----------

    private void GerarDias()
    {
        var hoje = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).Date;
        for (var i = 0; i < DiasVisiveis; i++)
        {
            var data = hoje.AddDays(i);
            if (data.DayOfWeek == DiaFechado) continue;
            Dias.Add(new DiaItem(data, SelecionarDiaCommand));
        }
        MarcarDia(Dias.FirstOrDefault());
    }

    private void MarcarDia(DiaItem? dia)
    {
        if (DiaSelecionado is not null) DiaSelecionado.IsSelected = false;
        DiaSelecionado = dia;
        if (dia is not null) dia.IsSelected = true;
    }

    private async Task CarregarHorariosAsync()
    {
        if (_original is null || DiaSelecionado is null) return;

        var versao = ++_versaoCarga;
        var dia = DiaSelecionado.Data;
        var ocupados = await _agendamentos.ObterHorariosOcupadosAsync(_original.BarbeiroId, dia);
        if (versao != _versaoCarga) return;

        // O próprio agendamento não conta como ocupado
        if (dia.Date == _original.Data.Date)
            ocupados = ocupados.Where(o => o.InicioTs != _original.HorarioTs).ToList();

        _ocupados = ocupados;
        MontarHorarios();
    }

    private void MontarHorarios()
    {
        Horarios.Clear();
        HorarioSelecionado = null;
        if (_original is null || DiaSelecionado is null) return;

        var duracao = _original.FimTs - _original.HorarioTs;
        if (duracao <= TimeSpan.Zero) duracao = TimeSpan.FromMinutes(30);

        var passo = TimeSpan.FromMinutes(IntervaloMinutos);
        var agora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime;

        for (var t = Abertura; t + duracao <= Fechamento; t += passo)
        {
            var fim = t + duracao;
            var livre = DiaSelecionado.Data + t > agora &&
                        !_ocupados.Any(o => t < o.FimTs && o.InicioTs < fim);
            Horarios.Add(new HorarioItem(t, livre, SelecionarHorarioCommand));
        }

        SemHorarios = !Horarios.Any(h => h.Disponivel);
    }
}
