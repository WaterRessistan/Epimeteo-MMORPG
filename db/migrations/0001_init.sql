-- 0001_init.sql — cuentas, sesiones, rate limit de login, personajes e instancias de ítem.
-- Diseño cerrado en docs/02-esquema-bd.md. No editar esta migración una vez aplicada en ningún
-- entorno: la siguiente corrección va en 0002_....sql.

CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ── Cuentas y sesión ─────────────────────────────────────────────────────────

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

-- ── Personajes ───────────────────────────────────────────────────────────────

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

-- ── Ítems, inventario y equipo ───────────────────────────────────────────────

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
