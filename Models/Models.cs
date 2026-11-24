using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace LumosBarbearia.Models;

// =====================================================================================
// Mapeamento das tabelas do Supabase. Se algum nome de coluna do seu banco for diferente,
// ajuste SOMENTE o texto dentro de [Column("...")] / [Table("...")] aqui.
// =====================================================================================

/// <summary>Valores da coluna agendamentos.status.</summary>
public static class StatusAgendamento
{
    public const string Pendente = "pendente";
    public const string Confirmado = "confirmado";
    public const string Concluido = "concluido";
    public const string Cancelado = "cancelado";
}

/// <summary>Resultado simples de uma operação (evita exceções para erros de regra de negócio).</summary>
public record ResultadoOperacao(bool Sucesso, string Mensagem)
{
    public static ResultadoOperacao Ok(string mensagem = "") => new(true, mensagem);
    public static ResultadoOperacao Falha(string mensagem) => new(false, mensagem);
}

[Table("clientes")]
public class Cliente : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("auth_user_id")]
    public Guid AuthUserId { get; set; }

    [Column("nome")]
    public string Nome { get; set; } = string.Empty;

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("telefone")]
    public string Telefone { get; set; } = string.Empty;

    // O banco bloqueia escrita direta do aceite: ele é gravado por fn_registrar_consentimento.
    [Column("aceito_lgpd", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public bool AceitoLgpd { get; set; }
}

[Table("barbeiros")]
public class Barbeiro : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("nome")]
    public string Nome { get; set; } = string.Empty;

    /// <summary>Criado pelo Database/patch_app.sql: liga o barbeiro à conta de login dele.</summary>
    [Column("auth_user_id")]
    public Guid? AuthUserId { get; set; }
}

[Table("servicos")]
public class Servico : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("nome")]
    public string Nome { get; set; } = string.Empty;

    /// <summary>Duração em minutos.</summary>
    [Column("duracao")]
    public int Duracao { get; set; }

    [Column("preco")]
    public decimal Preco { get; set; }
}

[Table("barbeiro_servicos")]
public class BarbeiroServico : BaseModel
{
    [PrimaryKey("barbeiro_id", true)]
    public long BarbeiroId { get; set; }

    [Column("servico_id")]
    public long ServicoId { get; set; }
}

[Table("agendamentos")]
public class Agendamento : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("cliente_id")]
    public long ClienteId { get; set; }

    [Column("barbeiro_id")]
    public long BarbeiroId { get; set; }

    [Column("servico_id")]
    public long ServicoId { get; set; }

    [Column("data")]
    public DateTime Data { get; set; }

    [Column("horario")]
    public TimeSpan Horario { get; set; }

    [Column("status")]
    public string Status { get; set; } = StatusAgendamento.Pendente;
}

/// <summary>
/// Linha da view vw_agenda_app (Database/patch_app.sql): agendamento com nomes já resolvidos.
/// Usada no Balcão (todos os agendamentos, só barbeiro) e em Meus Horários (RLS filtra só os do cliente).
/// </summary>
[Table("vw_agenda_app")]
public class AgendaDetalhada : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("cliente_id")] public long ClienteId { get; set; }
    [Column("barbeiro_id")] public long BarbeiroId { get; set; }
    [Column("servico_id")] public long ServicoId { get; set; }
    [Column("data")] public DateTime Data { get; set; }

    [Column("cliente")] public string Cliente { get; set; } = string.Empty;
    [Column("telefone_cliente")] public string TelefoneCliente { get; set; } = string.Empty;
    [Column("servico")] public string Servico { get; set; } = string.Empty;
    [Column("barbeiro")] public string Barbeiro { get; set; } = string.Empty;
    [Column("preco")] public decimal Preco { get; set; }

    [Column("horario_ts")] public TimeSpan HorarioTs { get; set; }
    [Column("fim_ts")] public TimeSpan FimTs { get; set; }

    [Column("status")] public string Status { get; set; } = StatusAgendamento.Pendente;
}

/// <summary>Intervalo ocupado devolvido por fn_horarios_ocupados (sem dados pessoais).</summary>
public class HorarioOcupado
{
    [JsonProperty("inicio")] public TimeSpan InicioTs { get; set; }
    [JsonProperty("fim")] public TimeSpan FimTs { get; set; }
}
