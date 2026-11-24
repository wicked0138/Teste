using LumosBarbearia.Models;
using Newtonsoft.Json.Linq;
using Supabase.Postgrest;
using Supabase.Postgrest.Exceptions;
using static Supabase.Postgrest.Constants;

namespace LumosBarbearia.Services;

/// <summary>Consultas e ações de agendamento do lado do cliente (usa as funções do banco).</summary>
public class AgendamentoService
{
    private readonly SupabaseService _supabase;

    public AgendamentoService(SupabaseService supabase) => _supabase = supabase;

    private Supabase.Client Db => _supabase.Client;

    // ---------- Catálogo (leitura pública) ----------

    public async Task<List<Barbeiro>> ObterBarbeirosAsync()
    {
        await _supabase.InicializarAsync();
        var r = await Db.From<Barbeiro>().Order("nome", Ordering.Ascending).Get();
        return r.Models;
    }

    public async Task<List<Servico>> ObterServicosAsync()
    {
        await _supabase.InicializarAsync();
        var r = await Db.From<Servico>().Order("nome", Ordering.Ascending).Get();
        return r.Models;
    }

    public async Task<List<BarbeiroServico>> ObterVinculosAsync()
    {
        await _supabase.InicializarAsync();
        var r = await Db.From<BarbeiroServico>().Get();
        return r.Models;
    }

    // ---------- Horários ocupados ----------

    /// <summary>Intervalos já reservados do barbeiro no dia (fn_horarios_ocupados: sem dados pessoais).</summary>
    public async Task<List<HorarioOcupado>> ObterHorariosOcupadosAsync(long barbeiroId, DateTime data)
    {
        await _supabase.InicializarAsync();

        var resposta = await Db.Rpc(DbContract.FnHorariosOcupados, new Dictionary<string, object>
        {
            [DbContract.PBarbeiro] = barbeiroId,
            [DbContract.PData] = data.ToString("yyyy-MM-dd")
        });

        var lista = new List<HorarioOcupado>();
        if (string.IsNullOrWhiteSpace(resposta?.Content)) return lista;

        // Lê o JSON de forma tolerante: aceita variações de nome de coluna (inicio/inicio_ts/horario...).
        foreach (var item in JArray.Parse(resposta.Content).OfType<JObject>())
        {
            var inicio = Ler(item, "inicio", "inicio_ts", "horario_ts", "horario", "hora_inicio");
            var fim = Ler(item, "fim", "fim_ts", "hora_fim");
            if (inicio is not null && fim is not null)
                lista.Add(new HorarioOcupado { InicioTs = inicio.Value, FimTs = fim.Value });
        }
        return lista;
    }

    private static TimeSpan? Ler(JObject o, params string[] nomes)
    {
        foreach (var n in nomes)
        {
            var t = o[n];
            if (t is null || t.Type == JTokenType.Null) continue;
            var s = t.ToString();
            // Aceita "08:00:00" e também timestamps "2026-11-23T08:00:00"
            if (DateTime.TryParse(s, out var dt) && s.Contains('T')) return dt.TimeOfDay;
            if (TimeSpan.TryParse(s, out var ts)) return ts;
        }
        return null;
    }

    // ---------- Reservar / cancelar / remarcar ----------

    public async Task<ResultadoOperacao> CriarAsync(long clienteId, long barbeiroId, long servicoId, DateTime data, TimeSpan inicio)
    {
        await _supabase.InicializarAsync();
        try
        {
            await Db.Rpc(DbContract.FnAgendar, new Dictionary<string, object>
            {
                [DbContract.PCliente] = clienteId,
                [DbContract.PBarbeiro] = barbeiroId,
                [DbContract.PServico] = servicoId,
                [DbContract.PData] = data.ToString("yyyy-MM-dd"),
                [DbContract.PHorario] = inicio.ToString(@"hh\:mm\:ss")
            });
            return ResultadoOperacao.Ok("Seu horário foi reservado e aguarda confirmação do barbeiro.");
        }
        catch (PostgrestException ex)
        {
            return ResultadoOperacao.Falha(TraduzirErro(ex));
        }
    }

    public async Task<ResultadoOperacao> CancelarAsync(long agendamentoId)
    {
        await _supabase.InicializarAsync();
        try
        {
            await Db.Rpc(DbContract.FnCancelar, new Dictionary<string, object>
            {
                [DbContract.PAgendamento] = agendamentoId
            });
            return ResultadoOperacao.Ok("Agendamento cancelado.");
        }
        catch (PostgrestException ex)
        {
            return ResultadoOperacao.Falha(TraduzirErro(ex));
        }
    }

    public async Task<ResultadoOperacao> RemarcarAsync(long agendamentoId, DateTime data, TimeSpan inicio)
    {
        await _supabase.InicializarAsync();
        try
        {
            await Db.Rpc(DbContract.FnRemarcar, new Dictionary<string, object>
            {
                [DbContract.PAgendamento] = agendamentoId,
                [DbContract.PData] = data.ToString("yyyy-MM-dd"),
                [DbContract.PHorario] = inicio.ToString(@"hh\:mm\:ss")
            });
            return ResultadoOperacao.Ok("Horário remarcado. Ele volta para \"A Confirmar\".");
        }
        catch (PostgrestException ex)
        {
            return ResultadoOperacao.Falha(TraduzirErro(ex));
        }
    }

    // ---------- Meus horários ----------

    /// <summary>Agendamentos do cliente (a view respeita o RLS: cada cliente só enxerga os próprios).</summary>
    public async Task<List<AgendaDetalhada>> ObterDoClienteAsync(long clienteId)
    {
        await _supabase.InicializarAsync();
        var r = await Db.From<AgendaDetalhada>()
            .Filter("cliente_id", Operator.Equals, clienteId.ToString())
            .Order("data", Ordering.Descending)
            .Get();
        return r.Models;
    }

    // ---------- Mensagens de erro do banco em português ----------

    internal static string TraduzirErro(PostgrestException ex)
    {
        var t = ((ex.Message ?? "") + " " + (ex.Content ?? "")).ToLowerInvariant();

        if (t.Contains("ocupad") || t.Contains("indispon") || t.Contains("conflit") || t.Contains("overlap") || t.Contains("exclus") || t.Contains("já reserv") || t.Contains("ja reserv"))
            return "Esse horário acabou de ser reservado por outra pessoa. Escolha outro.";
        if (t.Contains("passado"))
            return "Esse horário já passou. Escolha um horário futuro.";
        if (t.Contains("08:00") || t.Contains("20:00") || t.Contains("expediente") || t.Contains("funcionamento") || t.Contains("fora do"))
            return "Esse horário está fora do funcionamento (08:00 às 20:00).";
        if (t.Contains("não realiza") || t.Contains("nao realiza") || t.Contains("barbeiro_servico"))
            return "Esse barbeiro não realiza o serviço escolhido.";
        if (t.Contains("permiss") || t.Contains("policy") || t.Contains("rls") || t.Contains("42501"))
            return "Você não tem permissão para esta ação.";
        if (t.Contains("cancelad") || t.Contains("status"))
            return "Este agendamento não pode mais ser alterado.";

        System.Diagnostics.Debug.WriteLine($"[Banco] Erro não mapeado: {ex.Message} | {ex.Content}");
        return "Não foi possível concluir a operação. Tente novamente.";
    }
}
