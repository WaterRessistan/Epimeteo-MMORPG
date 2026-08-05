-- 0003_shops_economy.sql — stock mutable de tiendas y el log de economía append-only
-- (docs/02 § Economía y tiendas, ya diseñadas desde la Fase 0; FASE-07-tiendas.md §3 sólo las
-- crea, no cambia el diseño). Las definiciones de tienda (qué vende, precio base) viven en
-- content/shops/*.json, no aquí — esta tabla sólo guarda lo mutable: cuánto queda y cuándo
-- repone (CLAUDE.md §3).

CREATE TABLE shop_stock (
    shop_key      text NOT NULL,
    def_key       text NOT NULL,
    stock         int  NOT NULL CHECK (stock >= 0),
    stock_max     int  NOT NULL,
    price_buy     bigint,        -- NULL = usar el precio de content/shops/*.json
    price_sell    bigint,
    restock_at    timestamptz,
    PRIMARY KEY (shop_key, def_key)
);

-- Log append-only. En un MMO esto no es opcional: sin él, no puedes investigar duplicación de
-- ítems ni deflación de la economía.
CREATE TABLE economy_log (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at            timestamptz NOT NULL DEFAULT now(),
    kind          smallint NOT NULL,   -- 1 compra, 2 venta, 3 loot, 4 tirar, 5 cosecha,
                                       -- 6 quest, 7 admin, 8 destruir, 9 trade
    character_id  bigint,
    def_key       text,
    quantity      int,
    gold_delta    bigint,
    gold_after    bigint,
    context       jsonb                -- shop_key, monster_key, entity_id, etc.
);
CREATE INDEX ON economy_log (character_id, at DESC);
CREATE INDEX ON economy_log (at DESC);
