-- =====================================================================================
-- Lumos Barbearia - patch do app. Rode no SQL Editor DEPOIS do script principal
-- ("PROJETO BARBEARIA"). Idempotente: pode rodar mais de uma vez.
-- Cria: vínculo barbeiro<->login, permissões do barbeiro (Balcão), visitante vendo horários,
-- view da agenda do app, remarcar e Realtime.
-- =====================================================================================

-- 1) Quem é barbeiro: liga o barbeiro à conta de login dele.
ALTER TABLE barbeiros
    ADD COLUMN IF NOT EXISTS auth_user_id UUID UNIQUE REFERENCES auth.users(id) ON DELETE SET NULL;

CREATE OR REPLACE FUNCTION is_staff()
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (SELECT 1 FROM barbeiros WHERE auth_user_id = auth.uid());
$$;
REVOKE EXECUTE ON FUNCTION is_staff() FROM PUBLIC, anon;
GRANT  EXECUTE ON FUNCTION is_staff() TO authenticated;

-- Para uma conta ver o Balcão:
--   UPDATE barbeiros SET auth_user_id = '<uuid do usuário em Authentication>' WHERE id = 1;

-- 2) Permissões do barbeiro (RLS): vê todos os agendamentos e clientes e muda o status.
DROP POLICY IF EXISTS staff_le_agendamentos ON agendamentos;
CREATE POLICY staff_le_agendamentos ON agendamentos
    FOR SELECT TO authenticated USING (is_staff());

DROP POLICY IF EXISTS staff_atualiza_agendamentos ON agendamentos;
CREATE POLICY staff_atualiza_agendamentos ON agendamentos
    FOR UPDATE TO authenticated USING (is_staff()) WITH CHECK (is_staff());

DROP POLICY IF EXISTS staff_le_clientes ON clientes;
CREATE POLICY staff_le_clientes ON clientes
    FOR SELECT TO authenticated USING (is_staff());

-- 3) Visitante (sem login) enxerga horários ocupados antes de criar conta.
--    A função só devolve início e fim, sem dados pessoais.
GRANT EXECUTE ON FUNCTION fn_horarios_ocupados(BIGINT, DATE) TO anon;

-- 4) View usada pelo Balcão (barbeiro vê todas) e por Meus Horários (o RLS limita ao cliente).
--    security_invoker = true: respeita as policies de quem consulta.
CREATE OR REPLACE VIEW vw_agenda_app
WITH (security_invoker = true)
AS
SELECT
    a.id,
    a.cliente_id,
    a.barbeiro_id,
    a.servico_id,
    a.data,
    c.nome            AS cliente,
    c.telefone        AS telefone_cliente,
    s.nome            AS servico,
    b.nome            AS barbeiro,
    s.preco           AS preco,
    a.horario         AS horario_ts,
    a.fim_horario     AS fim_ts,
    a.status
FROM agendamentos a
JOIN clientes  c ON c.id = a.cliente_id
JOIN servicos  s ON s.id = a.servico_id
JOIN barbeiros b ON b.id = a.barbeiro_id;

GRANT SELECT ON vw_agenda_app TO authenticated;

-- 5) Remarcar: o cliente só mexe no PRÓPRIO agendamento (pendente/confirmado).
--    Volta para "pendente". O trigger trg_agend_valida (08:00-20:00, passado, barbeiro/serviço)
--    e a exclusion constraint (sobreposição) disparam normalmente no UPDATE.
CREATE OR REPLACE FUNCTION fn_remarcar_agendamento(
    p_agendamento BIGINT,
    p_data        DATE,
    p_horario     TIME
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    UPDATE agendamentos a
       SET data = p_data,
           horario = p_horario,
           status = 'pendente'
     WHERE a.id = p_agendamento
       AND a.status IN ('pendente', 'confirmado')
       AND EXISTS (
           SELECT 1 FROM clientes c
            WHERE c.id = a.cliente_id
              AND c.auth_user_id = auth.uid()
       );

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Agendamento não encontrado ou não pode ser remarcado.';
    END IF;

EXCEPTION
    WHEN exclusion_violation THEN
        RAISE EXCEPTION 'Horário indisponível: o barbeiro já possui atendimento nesse período.';
END;
$$;

REVOKE EXECUTE ON FUNCTION fn_remarcar_agendamento(BIGINT, DATE, TIME) FROM PUBLIC, anon;
GRANT  EXECUTE ON FUNCTION fn_remarcar_agendamento(BIGINT, DATE, TIME) TO authenticated;

-- 6) Realtime em agendamentos (ignora se já estiver ligado)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_publication_tables
         WHERE pubname = 'supabase_realtime' AND schemaname = 'public' AND tablename = 'agendamentos'
    ) THEN
        ALTER PUBLICATION supabase_realtime ADD TABLE agendamentos;
    END IF;
END $$;
