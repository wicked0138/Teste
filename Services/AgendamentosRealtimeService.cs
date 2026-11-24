using LumosBarbearia.Models;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;
using static Supabase.Realtime.PostgresChanges.PostgresChangesOptions;

namespace LumosBarbearia.Services;

/// <summary>
/// Escuta mudanças na tabela agendamentos (Realtime) e avisa as telas inscritas.
/// O Realtime respeita o RLS: o barbeiro recebe tudo, o cliente só as linhas dele.
/// Vários consumidores (Balcão e Meus Horários) compartilham a mesma assinatura.
/// </summary>
public class AgendamentosRealtimeService
{
    private readonly SupabaseService _supabase;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RealtimeChannel? _canal;
    private EventHandler? _handlers;

    public AgendamentosRealtimeService(SupabaseService supabase)
    {
        _supabase = supabase;
        // Ao logar/deslogar o token muda: refaz a assinatura para valer com a nova identidade
        _supabase.AutenticacaoAlterada += async (_, _) => await ReiniciarAsync();
    }

    public event EventHandler? AgendamentoAlterado
    {
        add => _handlers += value;
        remove => _handlers -= value;
    }

    /// <summary>Inicia a assinatura (idempotente).</summary>
    public async Task IniciarAsync()
    {
        await _supabase.InicializarAsync();
        await _lock.WaitAsync();
        try
        {
            if (_canal is not null) return;

            await _supabase.Client.Realtime.ConnectAsync();

            _canal = _supabase.Client.Realtime.Channel("realtime", "public", "agendamentos");
            _canal.AddPostgresChangeHandler(ListenType.All, (_, _) => _handlers?.Invoke(this, EventArgs.Empty));
            await _canal.Subscribe();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Encerra a assinatura, mas só se ninguém mais estiver ouvindo.</summary>
    public void Parar()
    {
        if (_handlers is not null) return; // outra tela ainda precisa
        EncerrarCanal();
    }

    private async Task ReiniciarAsync()
    {
        try
        {
            EncerrarCanal();
            if (_handlers is not null) await IniciarAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Realtime] Falha ao reiniciar: {ex.Message}");
        }
    }

    private void EncerrarCanal()
    {
        try { _canal?.Unsubscribe(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Realtime] Falha ao encerrar: {ex.Message}"); }
        _canal = null;
    }
}
