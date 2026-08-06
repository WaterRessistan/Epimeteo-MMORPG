-- 0004_farm.sql — granja y cultivos (docs/02 § Granja y cultivos, diseñadas desde la Fase 0;
-- FASE-08-granja-cultivos.md §3 sólo las crea, con una tabla nueva: farm_calendar).
--
-- fertilizer_key y harvests_left se quedan en el esquema tal como lo fijó docs/02 pero sin
-- lógica todavía (FASE-08 §2 D10): ningún cultivo de esta fase las usa.

-- Una parcela por personaje (o comunitaria, con owner NULL) — docs/02. Esta fase sólo crea la
-- comunitaria de map.village (FASE-08 §2 D2): la geometría vive aquí, no en content/, porque
-- owner_char_id ya anticipa que qué parcelas existen es estado mutable, no una decisión de mapa.
CREATE TABLE farm_plots (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    map_key        text NOT NULL,
    owner_char_id  bigint REFERENCES characters(id) ON DELETE CASCADE,
    origin_x       int NOT NULL,
    origin_y       int NOT NULL,
    width          int NOT NULL,
    height         int NOT NULL,
    UNIQUE (map_key, origin_x, origin_y)
);

CREATE TABLE farm_tiles (
    plot_id        bigint NOT NULL REFERENCES farm_plots(id) ON DELETE CASCADE,
    tile_x         int NOT NULL,          -- absoluto en el mapa, no relativo a la parcela
    tile_y         int NOT NULL,
    state          smallint NOT NULL DEFAULT 0,  -- 0 virgen 1 arado 2 plantado 3 listo
    crop_key       text,
    planted_at     timestamptz,
    watered_at     timestamptz,           -- NULL = sin regar en el día de granja actual
    growth_days    real NOT NULL DEFAULT 0,   -- progreso acumulado; +1,0 regado / +0,5 sin regar
    growth_needed  real NOT NULL DEFAULT 0,   -- copiado de la definición al plantar, para
                                              -- que un rebalanceo no rompa cosechas en curso
    water_streak   smallint NOT NULL DEFAULT 0,   -- días regados seguidos → bonus de calidad
    fertilizer_key text,                  -- sin lógica todavía (FASE-08 §2 D10)
    harvests_left  smallint,              -- sin lógica todavía (FASE-08 §2 D10)
    eta_at         timestamptz,           -- estimación para la UI, recalculada al plantar y en
                                          -- cada barrido diario (FASE-08 §2 D12)
    PRIMARY KEY (plot_id, tile_x, tile_y)
);
CREATE INDEX ON farm_tiles (plot_id);
CREATE INDEX ON farm_tiles (state) WHERE state = 2;

-- Fila única: el último día de granja (frontera de las 05:00 UTC) ya cerrado por el barrido del
-- tick (FASE-08 §2 D1 — no es el UPDATE masivo que describía docs/00 §7, que habría sido un
-- segundo escritor de farm_tiles compitiendo con el guardado async de las acciones del jugador).
-- Sirve para recuperar días perdidos si el servidor estuvo caído: se procesan uno a uno todos
-- los límites que hayan pasado desde este valor.
CREATE TABLE farm_calendar (
    id              smallint PRIMARY KEY DEFAULT 1,
    last_day_index  int NOT NULL,
    CONSTRAINT farm_calendar_singleton CHECK (id = 1)
);

-- Parcela comunitaria de map.village: hueco abierto al sur del pueblo, fuera de los edificios y
-- del NPC de la armería/tienda general (Fase 7) y de la región "plaza". 8×6 tiles.
INSERT INTO farm_plots (map_key, owner_char_id, origin_x, origin_y, width, height)
VALUES ('map.village', NULL, 6, 82, 8, 6)
ON CONFLICT (map_key, origin_x, origin_y) DO NOTHING;

-- last_day_index inicial = el día de granja de "ahora" en el momento del despliegue, con la
-- misma fórmula que FarmCalendar.DayIndex en C# (frontera de referencia 2000-01-01T05:00:00Z):
-- así el primer arranque no interpreta toda la historia previa a esta fase como "días perdidos".
INSERT INTO farm_calendar (id, last_day_index)
VALUES (1, floor(extract(epoch FROM (now() - timestamptz '2000-01-01 05:00:00+00')) / 86400)::int)
ON CONFLICT (id) DO NOTHING;
