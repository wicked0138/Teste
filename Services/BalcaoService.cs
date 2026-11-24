using LumosBarbearia.Models;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace LumosBarbearia.Services;

/// <summary>Operações do Balcão (barbeiro). O RLS do banco decide quem pode ver/alterar.</summary>
public class BalcaoService
{
    private readonly SupabaseService _supabase;

    public BalcaoService(SupabaseService supabase) => _supabase = supabase;

    /// <summary>Todos os agendamentos do dia, em ordem de horário.</summary>
    public async Task<List<AgendaDetalhada>> ObterAgendaAsync(DateTime dia)
    {
        await _supabase.InicializarAsync();
        var r = await _supabase.Client.From<AgendaDetalhada>()
            .Filter("data", Operator.Equals, dia.ToString("yyyy-MM-dd"))
            .Order("horario_ts", Ordering.Ascending)
            .Get();
        return r.Models;
    }

    /// <summary>Altera o status. Retorna false se nenhuma linha foi alterada (sem permissão pelo RLS).</summary>
    public async Task<bool> AtualizarStatusAsync(long agendamentoId, string novoStatus)
    {
        await _supabase.InicializarAsync();
        var r = await _supabase.Client.From<Agendamento>()
            .Where(a => a.Id == agendamentoId)
            .Set(a => a.Status, novoStatus)
            .Update();
        return r.Models.Count > 0;
    }
}
