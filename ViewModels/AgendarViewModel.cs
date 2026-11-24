using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumosBarbearia.Models;
using LumosBarbearia.Services;

namespace LumosBarbearia.ViewModels;

/// <summary>
/// ViewModel da aba "Agendar": dia → barbeiro → serviço → horário → confirmar no bottom sheet.
/// A grade é recalculada localmente (dia + barbeiro + serviço + ocupados em cache); os horários
/// ocupados só são buscados no servidor ao trocar de dia/barbeiro ou após uma reserva.
/// As regras finais (horário comercial, passado, sobreposição) são garantidas pelo banco.
/// </summary>
public partial class AgendarViewModel : ObservableObject
{
    // ---------- Regras de funcionamento (devem refletir o trigger do banco: 08:00–20:00) ----------
    private const int DiasVisiveis = 14;
    private const int IntervaloMinutos = 30;
    private static readonly TimeSpan Abertura = TimeSpan.FromHours(8);
    private static readonly TimeSpan Fechamento = TimeSpan.FromHours(20);
    private static readonly DayOfWeek DiaFechado = DayOfWeek.Sunday;

    private static readonly CultureInfo PtBr = new("pt-BR");
    // O banco compara com a hora de Brasília; a grade usa o mesmo fuso, independente do aparelho.
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly SupabaseService _supabase;
    private readonly AgendamentoService _agendamentos;
    private readonly IDialogService _dialogos;
    private readonly INavigationService _navegacao;

    private List<Servico> _servicos = new();
    private List<BarbeiroServico> _vinculos = new();
    private List<HorarioOcupado> _ocupados = new();
    private int _versaoCarga; // descarta respostas antigas se o usuário trocar de dia/barbeiro rapidamente

    public AgendarViewModel(
        SupabaseService supabase,
        AgendamentoService agendamentos,
        IDialogService dialogos,
        INavigationService navegacao)
    {
        _supabase = supabase;
        _agendamentos = agendamentos;
        _dialogos = dialogos;
        _navegacao = navegacao;
    }

    // ---------- Coleções exibidas ----------
    public ObservableCollection<DiaItem> Dias { get; } = new();
    public ObservableCollection<BarbeiroItem> Barbeiros { get; } = new();
    public ObservableCollection<ServicoItem> Servicos { get; } = new();
    public ObservableCollection<HorarioItem> Horarios { get; } = new();

    // ---------- Estado ----------
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isReservando;
    [ObservableProperty] private bool _semHorarios;
    [ObservableProperty] private bool _isSheetOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErro))]
    private string? _erroMensagem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumoData))]
    private DiaItem? _diaSelecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumoBarbeiro))]
    private BarbeiroItem? _barbeiroSelecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumoServico))]
    [NotifyPropertyChangedFor(nameof(ResumoDuracao))]
    [NotifyPropertyChangedFor(nameof(ResumoPreco))]
    private ServicoItem? _servicoSelecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumoHorario))]
    private HorarioItem? _horarioSelecionado;

    public bool HasErro => !string.IsNullOrWhiteSpace(ErroMensagem);

    // ---------- Textos do resumo (bottom sheet) ----------
    public string ResumoBarbeiro => BarbeiroSelecionado?.Nome ?? "-";
    public string ResumoServico => ServicoSelecionado?.Nome ?? "-";
    public string ResumoDuracao => ServicoSelecionado?.DuracaoTexto ?? "-";
    public string ResumoPreco => ServicoSelecionado?.PrecoTexto ?? "-";
    public string ResumoHorario => HorarioSelecionado?.Texto ?? "-";

    public string ResumoData
    {
        get
        {
            if (DiaSelecionado is null) return "-";
            var texto = DiaSelecionado.Data.ToString("dddd, dd 'de' MMMM", PtBr);
            return char.ToUpper(texto[0], PtBr) + texto[1..];
        }
    }

    // =====================================================================
    // CARGA INICIAL (chamada pela página em OnAppearing)
    // =====================================================================
    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErroMensagem = null;

            if (Dias.Count == 0)
                GerarDias();

            if (Barbeiros.Count == 0)
            {
                var barbeirosTask = _agendamentos.ObterBarbeirosAsync();
                var servicosTask = _agendamentos.ObterServicosAsync();
                var vinculosTask = _agendamentos.ObterVinculosAsync();
                await Task.WhenAll(barbeirosTask, servicosTask, vinculosTask);

                _servicos = servicosTask.Result;
                _vinculos = vinculosTask.Result;

                foreach (var b in barbeirosTask.Result)
                    Barbeiros.Add(new BarbeiroItem(b, SelecionarBarbeiroCommand));

                MarcarBarbeiro(Barbeiros.FirstOrDefault());
                ReconstruirServicos();
            }

            await CarregarHorariosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Agendar] Falha ao carregar: {ex}");
            ErroMensagem = "Não foi possível carregar os horários. Verifique sua conexão e tente novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =====================================================================
    // COMANDOS DE SELEÇÃO
    // =====================================================================

    [RelayCommand]
    private async Task SelecionarDiaAsync(DiaItem? dia)
    {
        if (dia is null || dia == DiaSelecionado) return;

        MarcarDia(dia);
        HorarioSelecionado = null;
        await RecarregarHorariosComSegurancaAsync();
    }

    /// <summary>Trocar de barbeiro muda os serviços oferecidos e a agenda ocupada.</summary>
    [RelayCommand]
    private async Task SelecionarBarbeiroAsync(BarbeiroItem? barbeiro)
    {
        if (barbeiro is null || barbeiro == BarbeiroSelecionado) return;

        MarcarBarbeiro(barbeiro);
        HorarioSelecionado = null;
        ReconstruirServicos();
        await RecarregarHorariosComSegurancaAsync();
    }

    /// <summary>A duração do serviço define quais horários cabem na agenda (sem ir à rede).</summary>
    [RelayCommand]
    private void SelecionarServico(ServicoItem? servico)
    {
        if (servico is null || servico == ServicoSelecionado) return;

        MarcarServico(servico);
        HorarioSelecionado = null;
        MontarHorarios();
    }

    /// <summary>Seleciona um horário livre e abre o bottom sheet com o resumo.</summary>
    [RelayCommand]
    private void SelecionarHorario(HorarioItem? horario)
    {
        if (horario is null || !horario.Disponivel) return;

        if (ServicoSelecionado is null || BarbeiroSelecionado is null)
        {
            ErroMensagem = "Escolha o barbeiro e o serviço primeiro.";
            return;
        }

        ErroMensagem = null;

        if (HorarioSelecionado is not null)
            HorarioSelecionado.IsSelected = false;

        horario.IsSelected = true;
        HorarioSelecionado = horario;
        IsSheetOpen = true;
    }

    [RelayCommand]
    private void FecharSheet() => IsSheetOpen = false;

    // =====================================================================
    // RESERVA
    // =====================================================================

    /// <summary>Confirma a reserva: chama fn_agendar (status "pendente" = "A Confirmar").</summary>
    [RelayCommand]
    private async Task ReservarAsync()
    {
        if (IsReservando || HorarioSelecionado is null || ServicoSelecionado is null
            || BarbeiroSelecionado is null || DiaSelecionado is null)
            return;

        // 1) Precisa estar logado
        if (!_supabase.EstaLogado)
        {
            IsSheetOpen = false;
            await _dialogos.AlertAsync("Entre na sua conta", "Faça login para concluir a reserva.");
            await _navegacao.GoToAsync("login");
            return;
        }

        try
        {
            IsReservando = true;

            // 2) Precisa ter o cadastro (telefone + LGPD). Login com Google sozinho não cria a linha em "clientes".
            var cliente = await _supabase.ObterClienteAsync();
            if (cliente is null || !cliente.AceitoLgpd)
            {
                IsSheetOpen = false;
                await _dialogos.AlertAsync("Complete seu cadastro", "Precisamos do seu telefone e do aceite da LGPD para agendar.");
                await _navegacao.GoToAsync("cadastro");
                return;
            }

            // 3) Reserva
            var resultado = await _agendamentos.CriarAsync(
                cliente.Id,
                BarbeiroSelecionado.Barbeiro.Id,
                ServicoSelecionado.Servico.Id,
                DiaSelecionado.Data,
                HorarioSelecionado.Inicio);

            IsSheetOpen = false;
            HorarioSelecionado = null;
            await RecarregarHorariosComSegurancaAsync(); // o horário reservado (ou em conflito) já aparece ocupado

            if (resultado.Sucesso)
            {
                await _dialogos.AlertAsync("Reservado!", resultado.Mensagem);
                await _navegacao.GoToAsync("//meus");
            }
            else
            {
                await _dialogos.AlertAsync("Ops", resultado.Mensagem);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Agendar] Falha ao reservar: {ex}");
            IsSheetOpen = false;
            await _dialogos.AlertAsync("Ops", "Não foi possível concluir a reserva. Tente novamente.");
        }
        finally
        {
            IsReservando = false;
        }
    }

    // =====================================================================
    // LÓGICA INTERNA
    // =====================================================================

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

    private void MarcarBarbeiro(BarbeiroItem? barbeiro)
    {
        if (BarbeiroSelecionado is not null) BarbeiroSelecionado.IsSelected = false;
        BarbeiroSelecionado = barbeiro;
        if (barbeiro is not null) barbeiro.IsSelected = true;
    }

    private void MarcarServico(ServicoItem? servico)
    {
        if (ServicoSelecionado is not null) ServicoSelecionado.IsSelected = false;
        ServicoSelecionado = servico;
        if (servico is not null) servico.IsSelected = true;
    }

    /// <summary>Lista só os serviços que o barbeiro escolhido realiza (tabela barbeiro_servicos).</summary>
    private void ReconstruirServicos()
    {
        var anterior = ServicoSelecionado?.Servico.Id;
        Servicos.Clear();
        ServicoSelecionado = null;

        if (BarbeiroSelecionado is null) return;

        var ids = _vinculos
            .Where(v => v.BarbeiroId == BarbeiroSelecionado.Barbeiro.Id)
            .Select(v => v.ServicoId)
            .ToHashSet();

        foreach (var s in _servicos.Where(s => ids.Contains(s.Id)))
            Servicos.Add(new ServicoItem(s, SelecionarServicoCommand));

        // Mantém o serviço anterior se o novo barbeiro também o realiza
        MarcarServico(Servicos.FirstOrDefault(s => s.Servico.Id == anterior) ?? Servicos.FirstOrDefault());
    }

    private async Task RecarregarHorariosComSegurancaAsync()
    {
        try
        {
            ErroMensagem = null;
            await CarregarHorariosAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Agendar] Falha ao atualizar horários: {ex}");
            ErroMensagem = "Não foi possível atualizar os horários.";
        }
    }

    /// <summary>Busca os horários ocupados do barbeiro/dia selecionados e remonta a grade.</summary>
    private async Task CarregarHorariosAsync()
    {
        if (DiaSelecionado is null || BarbeiroSelecionado is null) return;

        var versao = ++_versaoCarga;
        var ocupados = await _agendamentos.ObterHorariosOcupadosAsync(
            BarbeiroSelecionado.Barbeiro.Id, DiaSelecionado.Data);

        if (versao != _versaoCarga) return; // chegou uma resposta mais nova; ignora esta

        _ocupados = ocupados;
        MontarHorarios();
    }

    /// <summary>
    /// Gera a grade do dia. Um horário está disponível se (1) ainda não passou, (2) o serviço termina
    /// até o fechamento e (3) não se sobrepõe a nenhuma reserva do barbeiro.
    /// </summary>
    private void MontarHorarios()
    {
        Horarios.Clear();
        HorarioSelecionado = null;

        if (DiaSelecionado is null || ServicoSelecionado is null)
        {
            SemHorarios = false;
            return;
        }

        var duracao = TimeSpan.FromMinutes(ServicoSelecionado.Servico.Duracao);
        var passo = TimeSpan.FromMinutes(IntervaloMinutos);
        var agora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime;

        for (var t = Abertura; t + duracao <= Fechamento; t += passo)
        {
            var fim = t + duracao;

            var livre = DiaSelecionado.Data + t > agora &&
                        !_ocupados.Any(o => t < o.FimTs && o.InicioTs < fim); // sobreposição de intervalos

            Horarios.Add(new HorarioItem(t, livre, SelecionarHorarioCommand));
        }

        SemHorarios = !Horarios.Any(h => h.Disponivel);
    }
}
