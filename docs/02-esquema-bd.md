# 02 — Esquema de base de datos (diseño inicial)

> **Esto es diseño, no una migración lista para ejecutar.** El SQL definitivo se escribe en la
> Fase 2 (`db/migrations/0001_init.sql`) y puede ajustarse. PostgreSQL 16.

## Principio rector: definiciones vs. estado

| | Dónde vive | Ejemplo |
|---|---|---|
| **Definición** (inmutable, contenido) | `content/*.json` en git | "la espada de hierro hace 12 de daño y cuesta 150" |
| **Estado** (mutable, por jugador) | PostgreSQL | "el personaje 42 tiene 3 espadas de hierro, una con 40/50 de durabilidad" |

La BD referencia definiciones por `def_key` (`text`, ej. `weapon.iron_sword`). Sin FK a una tabla
de ítems: la validación de que la clave existe la hace el validador de contenido al arrancar el
servidor, no el motor SQL. Ventaja: rebalancear el juego es un commit, no una migración.

Existe una tabla `content_keys` **sembrada automáticamente al arranque** desde los JSON, sólo para
que las consultas analíticas y los `JOIN` de informes tengan algo contra lo que unir. No es fuente
de verdad.

---

## Cuentas y sesión

```sql
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE accounts (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username        citext NOT NULL UNIQUE,
    email           citext UNIQUE,
    password_hash   text   NOT NULL,          -- argon2id, incluye sal y parámetros
    password_ver    smallint NOT NULL DEFAULT 1,
    status          smallint NOT NULL DEFAULT 0,  -- 0 activa, 1 suspendida, 2 baneada, 3 borrada
    banned_until    timestamptz,
    ban_reason      text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    last_login_at   timestamptz,
    last_login_ip   inet,
    totp_secret     text,                     -- 2FA opcional, fase futura
    CONSTRAINT username_format CHECK (length(username) BETWEEN 3 AND 20)
);

-- Tokens de sesión persistentes: permiten reconectar sin reenviar la contraseña
-- y revocar sesiones desde el panel de admin.
CREATE TABLE account_sessions (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id   bigint NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    token_hash   bytea  NOT NULL,             -- SHA-256 del token; el token en claro no se guarda
    issued_at    timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL,
    revoked_at   timestamptz,
    ip           inet,
    user_agent   text
);
CREATE INDEX ON account_sessions (account_id) WHERE revoked_at IS NULL;
CREATE UNIQUE INDEX ON account_sessions (token_hash);

-- Rate limit de login por IP, persistido para sobrevivir a reinicios
CREATE TABLE login_attempts (
    ip           inet NOT NULL,
    attempted_at timestamptz NOT NULL DEFAULT now(),
    username     citext,
    success      boolean NOT NULL
);
CREATE INDEX ON login_attempts (ip, attempted_at DESC);
```

---

## Personajes

```sql
CREATE TABLE characters (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id      bigint NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    slot            smallint NOT NULL CHECK (slot BETWEEN 0 AND 4),   -- máximo 5 personajes
    name            citext NOT NULL,
    class_key       text   NOT NULL,        -- 'class.warrior' | 'class.mage' | 'class.hybrid'
    appearance      jsonb  NOT NULL DEFAULT '{}'::jsonb,  -- pelo, tono de piel, paleta

    level           int    NOT NULL DEFAULT 1,
    xp              bigint NOT NULL DEFAULT 0,
    -- Stats base asignados. Los derivados (ataque, defensa) NO se guardan:
    -- se recalculan al cargar desde base + clase + equipo + buffs.
    stat_str        int NOT NULL DEFAULT 0,
    stat_int        int NOT NULL DEFAULT 0,
    stat_vit        int NOT NULL DEFAULT 0,
    stat_dex        int NOT NULL DEFAULT 0,
    stat_points     int NOT NULL DEFAULT 0,  -- sin gastar

    hp              int NOT NULL DEFAULT 100,
    mp              int NOT NULL DEFAULT 50,
    gold            bigint NOT NULL DEFAULT 0 CHECK (gold >= 0),

    map_key         text NOT NULL DEFAULT 'map.village',
    pos_x           real NOT NULL DEFAULT 0,
    pos_y           real NOT NULL DEFAULT 0,
    facing          smallint NOT NULL DEFAULT 2,   -- 0 N, 1 E, 2 S, 3 O

    playtime_secs   bigint NOT NULL DEFAULT 0,
    created_at      timestamptz NOT NULL DEFAULT now(),
    last_played_at  timestamptz,
    deleted_at      timestamptz          -- borrado lógico; libera el nombre sólo tras purga
);

-- Un personaje vivo por slot y cuenta → impone el máximo de 5 sin contar filas
CREATE UNIQUE INDEX characters_account_slot_uq
    ON characters (account_id, slot) WHERE deleted_at IS NULL;
-- Nombre único entre personajes vivos
CREATE UNIQUE INDEX characters_name_uq
    ON characters (name) WHERE deleted_at IS NULL;
CREATE INDEX ON characters (account_id) WHERE deleted_at IS NULL;
```

**Nota de diseño:** los stats derivados (daño, defensa, velocidad) nunca se persisten. Se
recalculan a partir de stats base + clase + equipo. Si algún día rebalanceas una clase, todos los
personajes se ajustan solos.

```sql
-- Habilidades desbloqueadas / niveles de skill (fase 10)
CREATE TABLE character_skills (
    character_id bigint NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    skill_key    text   NOT NULL,
    skill_level  smallint NOT NULL DEFAULT 1,
    unlocked_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (character_id, skill_key)
);

-- Flags, quests, contadores y cualquier estado disperso. Evita 20 tablas de una columna.
CREATE TABLE character_state (
    character_id bigint NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    scope        text   NOT NULL,        -- 'quest' | 'flag' | 'counter' | 'cooldown'
    key          text   NOT NULL,
    value        jsonb  NOT NULL,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (character_id, scope, key)
);
```

---

## Ítems, inventario y equipo

Una sola tabla de instancias. El "inventario separado de armas y armaduras" es una cuestión de
`container`, no de tablas distintas — así mover un ítem entre bolsas es un `UPDATE` y no una
migración de fila entre tablas.

```sql
-- container:
--   0 general   1 bolsa de armas   2 bolsa de armaduras   3 equipado
--   4 banco     5 stock de tienda  6 buzón/correo         7 saco de loot
CREATE TABLE item_instances (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    def_key       text   NOT NULL,
    owner_char_id bigint REFERENCES characters(id) ON DELETE CASCADE,
    owner_acct_id bigint REFERENCES accounts(id)   ON DELETE CASCADE,  -- banco compartido
    container     smallint NOT NULL,
    slot          smallint NOT NULL,     -- en container=3 (equipado), slot = EquipSlot
    quantity      int    NOT NULL DEFAULT 1 CHECK (quantity > 0),
    durability    int,                   -- NULL = no se desgasta
    durability_max int,
    quality       smallint NOT NULL DEFAULT 0,  -- 0 normal, 1 fino, 2 raro...
    affixes       jsonb NOT NULL DEFAULT '[]'::jsonb,  -- modificadores tirados al generar
    bound_to      bigint REFERENCES characters(id),    -- ligado al personaje, no comerciable
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT one_owner CHECK (num_nonnulls(owner_char_id, owner_acct_id) = 1)
);

-- No puede haber dos ítems en el mismo hueco del mismo contenedor
CREATE UNIQUE INDEX item_slot_char_uq
    ON item_instances (owner_char_id, container, slot) WHERE owner_char_id IS NOT NULL;
CREATE UNIQUE INDEX item_slot_acct_uq
    ON item_instances (owner_acct_id, container, slot) WHERE owner_acct_id IS NOT NULL;
CREATE INDEX ON item_instances (def_key);
```

`EquipSlot` (valor de `slot` cuando `container = 3`):
`0 arma principal · 1 secundaria/escudo · 2 cabeza · 3 pecho · 4 manos · 5 piernas · 6 pies ·
7 capa · 8 anillo1 · 9 anillo2 · 10 amuleto · 11 herramienta`

**Apilables:** el tamaño de pila está en la definición JSON. La lógica de apilado vive en el
servidor (`InventorySystem`), no en la BD.

---

## Economía y tiendas

Las tiendas se definen en `content/shops/*.json` (qué venden, precios base, si tienen stock
infinito). La BD sólo guarda lo **mutable**: stock limitado y su reposición.

```sql
CREATE TABLE shop_stock (
    shop_key      text NOT NULL,
    def_key       text NOT NULL,
    stock         int  NOT NULL CHECK (stock >= 0),
    stock_max     int  NOT NULL,
    price_buy     bigint,        -- NULL = usar el precio de la definición JSON
    price_sell    bigint,
    restock_at    timestamptz,
    PRIMARY KEY (shop_key, def_key)
);

-- Log append-only. En un MMO esto no es opcional: sin él, no puedes investigar
-- duplicación de ítems ni deflación de la economía.
CREATE TABLE economy_log (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at            timestamptz NOT NULL DEFAULT now(),
    kind          smallint NOT NULL,   -- 1 compra, 2 venta, 3 loot, 4 tirar, 5 cosecha,
                                       -- 6 quest, 7 admin, 8 destruir, 9 trade, 10 reparar
                                       -- (10 añadido en la Fase 7: no estaba previsto en este
                                       -- diseño original — ver FASE-07-tiendas.md §2 D6)
    character_id  bigint,
    def_key       text,
    quantity      int,
    gold_delta    bigint,
    gold_after    bigint,
    context       jsonb                -- shop_key, monster_key, entity_id, etc.
);
CREATE INDEX ON economy_log (character_id, at DESC);
CREATE INDEX ON economy_log (at DESC);
```

Toda compra/venta se ejecuta en **una transacción SQL**: descontar oro, mover ítem, decrementar
stock y escribir el log. O todo, o nada.

---

## Granja y cultivos

Los cultivos tardan **~3 días reales**. Diseño clave: **el crecimiento no se simula por tick**.
El progreso avanza en un **job diario a las 05:00 UTC** que hace una sola sentencia `UPDATE` sobre
toda la tabla. 10.000 parcelas cuestan una consulta al día y 0 CPU el resto del tiempo.

Regar **acelera, no salva**: día regado = +1,0 de progreso; día sin regar = +0,5. El cultivo nunca
muere por descuido (ver `docs/00-arquitectura.md §7`).

```sql
-- Una parcela por personaje (o comunitaria, con owner NULL)
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
    fertilizer_key text,
    harvests_left  smallint,              -- cultivos multicosecha
    eta_at         timestamptz,           -- estimación para la UI, recalculada en el job diario
    PRIMARY KEY (plot_id, tile_x, tile_y)
);
CREATE INDEX ON farm_tiles (plot_id);
CREATE INDEX ON farm_tiles (state) WHERE state = 2;
```

**Job diario (05:00 UTC), dos sentencias para toda la granja del servidor:**

```sql
UPDATE farm_tiles
   SET growth_days  = growth_days + CASE WHEN watered_at >= :inicio_dia THEN 1.0 ELSE 0.5 END,
       water_streak = CASE WHEN watered_at >= :inicio_dia THEN water_streak + 1 ELSE 0 END,
       watered_at   = NULL
 WHERE state = 2;

UPDATE farm_tiles SET state = 3 WHERE state = 2 AND growth_days >= growth_needed;
```

`growth_needed` se copia de la definición JSON **al plantar**, no se lee en la cosecha: así
rebalancear un cultivo no altera lo que un jugador ya tiene sembrado.

---

## Mundo, monstruos y social

Los monstruos **no se persisten**: viven en memoria y se recrean desde `content/monsters/*.json`
y los puntos de spawn del mapa. Sólo se guarda lo que debe sobrevivir a un reinicio:

```sql
CREATE TABLE world_state (
    key        text PRIMARY KEY,
    value      jsonb NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);
-- ej: 'world.day', 'world.season', 'boss.forest_king.next_spawn_at'

-- Muertes en PvP: imprescindible para investigar griefing y para estadísticas.
-- Las muertes contra monstruos NO se guardan (demasiado volumen, poco valor).
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
CREATE INDEX ON combat_log (victim_id, at DESC);

-- Chat persistido sólo para moderación; se purga a los 30 días.
CREATE TABLE chat_log (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at           timestamptz NOT NULL DEFAULT now(),
    character_id bigint,
    channel      smallint NOT NULL,
    body         text NOT NULL
);
CREATE INDEX ON chat_log (at DESC);

-- Espacio reservado (fases posteriores, no implementar todavía):
--   friends(character_id, friend_id, since)
--   guilds / guild_members / guild_bank
--   mail(id, to_char_id, from_char_id, subject, body, gold, claimed_at)
--   trades(id, char_a, char_b, state, items jsonb, at)
```

---

## Notas operativas

- **Zona horaria:** todo `timestamptz` en UTC. La conversión es cosa del cliente.
- **Backup:** `pg_dump` diario comprimido + WAL archiving cuando haya jugadores reales.
  Se define en la Fase 5.
- **Migraciones:** ficheros `db/migrations/NNNN_descripcion.sql`, ejecutados por DbUp al arrancar
  el servidor, registrados en `schema_versions`. Nunca se edita una migración ya aplicada.
- **Índices:** los declarados aquí son el mínimo. No añadas más hasta tener un `EXPLAIN` que lo pida.
- **Cargas frecuentes:** entrar al mundo hace 4 consultas (character, item_instances,
  character_skills, character_state) + granja bajo demanda. Se puede reducir a una con un
  `SELECT` multi-resultado si alguna vez es un problema.
