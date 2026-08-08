-- 0007_anomaly_log.sql — anomalías de anticheat que cruzaron su umbral (FASE-13 §2 D7).
--
-- Hueco real de esquema: docs/02 no previó nada para esto. combat_log es sólo PvP y
-- admin_action_log es de la Fase 11; ninguna sirve para "esta sesión lleva 120 acciones fuera de
-- alcance en un minuto".
--
-- Sólo se escriben las que cruzan un umbral, no todos los rechazos: un cliente honesto falla
-- alguno de vez en cuando por latencia, y guardar eso sería llenar la tabla de ruido.
--
-- character_id y account_id son nullable a propósito: una anomalía puede ocurrir antes de que la
-- sesión haya elegido personaje (ProtocolError o InvalidState durante el handshake), y entonces
-- lo único que se sabe de quien la produjo es su IP.

CREATE TABLE anomaly_log (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at              timestamptz NOT NULL DEFAULT now(),
    session_id      int    NOT NULL,
    character_id    bigint REFERENCES characters(id) ON DELETE SET NULL,
    account_id      bigint REFERENCES accounts(id) ON DELETE SET NULL,
    kind            smallint NOT NULL,   -- Server/Security/AnomalyKind.cs
    count_in_window int    NOT NULL,     -- cuántas llevaba en la ventana de 60 s al cruzar
    action_taken    smallint NOT NULL,   -- 0 contada, 1 aviso, 2 desconexión
    remote_address  text,
    details         jsonb
);
CREATE INDEX ON anomaly_log (at DESC);
CREATE INDEX ON anomaly_log (character_id, at DESC);
CREATE INDEX ON anomaly_log (kind, at DESC);
