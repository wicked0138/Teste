namespace LumosBarbearia.Services;

/// <summary>Nomes das funções (RPC) do banco e dos seus parâmetros, conforme o schema do projeto.</summary>
public static class DbContract
{
    // fn_agendar(p_cliente, p_barbeiro, p_servico, p_data, p_horario) -> bigint
    public const string FnAgendar = "fn_agendar";
    public const string PCliente = "p_cliente";
    public const string PBarbeiro = "p_barbeiro";
    public const string PServico = "p_servico";
    public const string PData = "p_data";
    public const string PHorario = "p_horario";

    // fn_horarios_ocupados(p_barbeiro, p_data) -> (inicio, fim)
    public const string FnHorariosOcupados = "fn_horarios_ocupados";

    // fn_cancelar_agendamento(p_agendamento)
    public const string FnCancelar = "fn_cancelar_agendamento";
    public const string PAgendamento = "p_agendamento";

    // fn_remarcar_agendamento(p_agendamento, p_data, p_horario)  -> criada em Database/patch_app.sql
    public const string FnRemarcar = "fn_remarcar_agendamento";

    // fn_registrar_consentimento(p_finalidade, p_versao_termo, p_consentiu, p_origem)
    public const string FnConsentimento = "fn_registrar_consentimento";
    public const string PFinalidade = "p_finalidade";
    public const string PVersaoTermo = "p_versao_termo";
    public const string PConsentiu = "p_consentiu";
    public const string POrigem = "p_origem";
}
