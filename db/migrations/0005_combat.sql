-- 0005_combat.sql — registro de muertes en PvP (docs/02 § combat_log, diseñada desde la Fase 0;
-- FASE-09-combate-pvp.md §3 sólo la crea).
--
-- Sólo PvP: docs/02 es explícito en que las muertes contra monstruos no se guardan ("demasiado
-- volumen, poco valor"). Sirve para investigar griefing, no para estadísticas de farmeo.

CREATE TABLE combat_log (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at            timestamptz NOT NULL DEFAULT now(),
    victim_id     bigint NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    killer_id     bigint REFERENCES characters(id) ON DELETE SET NULL,
    map_key       text NOT NULL,
    region        text,
    victim_level  int,
    killer_level  int,
    xp_lost       bigint,
    context       jsonb
);
CREATE INDEX ON combat_log (killer_id, at DESC);
CREATE INDEX ON combat_log (at DESC);
CREATE INDEX ON combat_log (victim_id, at DESC);
