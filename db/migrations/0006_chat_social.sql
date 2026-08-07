-- 0006_chat_social.sql — chat, susurros y administración (FASE-11-chat-social.md).
--
-- chat_log estaba diseñada en docs/02-esquema-bd.md desde la Fase 0, sin migrar hasta ahora.
-- accounts.is_admin y admin_action_log son huecos reales de esta fase (§2 D6, D7): no había
-- ninguna columna de rol ni tabla de auditoría para kick/ban/teleport/give.

ALTER TABLE accounts ADD COLUMN is_admin boolean NOT NULL DEFAULT false;

-- Chat persistido sólo para moderación; se purga a los 30 días (docs/02, purga fuera de alcance
-- de esta fase — igual que combat_log no tiene todavía una tarea de limpieza).
CREATE TABLE chat_log (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at           timestamptz NOT NULL DEFAULT now(),
    character_id bigint REFERENCES characters(id) ON DELETE SET NULL,
    channel      smallint NOT NULL,
    body         text NOT NULL
);
CREATE INDEX ON chat_log (at DESC);

-- Todo lo que hace un administrador queda escrito, sin excepción (FASE-11 §2 D7).
-- action: 0 kick, 1 ban, 2 teleport, 3 give.
CREATE TABLE admin_action_log (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at                 timestamptz NOT NULL DEFAULT now(),
    admin_character_id bigint REFERENCES characters(id) ON DELETE SET NULL,
    admin_name         text NOT NULL,
    target_character_id bigint REFERENCES characters(id) ON DELETE SET NULL,
    target_name        text NOT NULL,
    action             smallint NOT NULL,
    reason             text,
    details            jsonb
);
CREATE INDEX ON admin_action_log (at DESC);
CREATE INDEX ON admin_action_log (admin_character_id, at DESC);
