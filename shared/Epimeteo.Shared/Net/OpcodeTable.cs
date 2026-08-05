using System.Collections.Frozen;

namespace Epimeteo.Shared.Net;

/// <summary>
/// Tabla estática que declara, para cada opcode, su familia y sus estados de sesión legales.
/// Es la única autoridad sobre "¿puede este cliente enviarme esto ahora mismo?".
/// Un mensaje recibido en estado ilegal se registra y cierra la conexión, sin excepciones.
/// </summary>
public static class OpcodeTable
{
    private static readonly FrozenDictionary<Opcode, OpcodeSpec> Specs = Build();

    /// <summary>Número de opcodes declarados en la tabla.</summary>
    public static int Count => Specs.Count;

    /// <summary>Todos los specs declarados, para tests y diagnóstico.</summary>
    public static IEnumerable<OpcodeSpec> All => Specs.Values;

    /// <summary>Busca el spec de un opcode. Falso si el opcode no existe en el protocolo.</summary>
    public static bool TryGet(Opcode opcode, out OpcodeSpec spec) => Specs.TryGetValue(opcode, out spec);

    /// <summary>
    /// Verdadero si el servidor debe aceptar <paramref name="opcode"/> estando en
    /// <paramref name="state"/>. Falso para opcodes desconocidos, opcodes S2C y estados ilegales.
    /// </summary>
    public static bool IsLegalFromClient(Opcode opcode, SessionState state)
    {
        if (!Specs.TryGetValue(opcode, out var spec) || !spec.IsClientToServer)
        {
            return false;
        }

        return (spec.LegalStates & state) != 0;
    }

    private static FrozenDictionary<Opcode, OpcodeSpec> Build()
    {
        const SessionState any = SessionState.Any;
        const SessionState none = SessionState.None;

        var specs = new OpcodeSpec[]
        {
            // ── Cliente → servidor ──────────────────────────────────────────
            new(Opcode.Hello, OpcodeFamily.Session, SessionState.Connecting),
            new(Opcode.Login, OpcodeFamily.Auth, SessionState.Greeted),
            new(Opcode.Register, OpcodeFamily.Auth, SessionState.Greeted),
            new(Opcode.Ping, OpcodeFamily.Session, any),

            new(Opcode.CharListRequest, OpcodeFamily.Character, SessionState.Authenticated),
            new(Opcode.CharCreate, OpcodeFamily.Character, SessionState.Authenticated),
            new(Opcode.CharDelete, OpcodeFamily.Character, SessionState.Authenticated),
            new(Opcode.CharSelect, OpcodeFamily.Character, SessionState.Authenticated),
            new(Opcode.WorldReady, OpcodeFamily.Character, SessionState.Loading),

            new(Opcode.InputState, OpcodeFamily.Movement, SessionState.InWorld),
            new(Opcode.Interact, OpcodeFamily.Movement, SessionState.InWorld),

            new(Opcode.InvMove, OpcodeFamily.Inventory, SessionState.InWorld),
            new(Opcode.InvUse, OpcodeFamily.Inventory, SessionState.InWorld),
            new(Opcode.InvDrop, OpcodeFamily.Inventory, SessionState.InWorld),
            new(Opcode.Equip, OpcodeFamily.Inventory, SessionState.InWorld),
            new(Opcode.Unequip, OpcodeFamily.Inventory, SessionState.InWorld),

            new(Opcode.ShopOpen, OpcodeFamily.Shop, SessionState.InWorld),
            new(Opcode.ShopBuy, OpcodeFamily.Shop, SessionState.InWorld),
            new(Opcode.ShopSell, OpcodeFamily.Shop, SessionState.InWorld),
            new(Opcode.ShopClose, OpcodeFamily.Shop, SessionState.InWorld),
            new(Opcode.ShopRepair, OpcodeFamily.Shop, SessionState.InWorld),

            new(Opcode.FarmPlant, OpcodeFamily.Farm, SessionState.InWorld),
            new(Opcode.FarmWater, OpcodeFamily.Farm, SessionState.InWorld),
            new(Opcode.FarmHarvest, OpcodeFamily.Farm, SessionState.InWorld),
            new(Opcode.FarmTill, OpcodeFamily.Farm, SessionState.InWorld),

            new(Opcode.Attack, OpcodeFamily.Combat, SessionState.InWorld),
            new(Opcode.SkillCast, OpcodeFamily.Combat, SessionState.InWorld),

            new(Opcode.ChatSend, OpcodeFamily.Chat, SessionState.InWorld),

            // ── Servidor → cliente: nunca legales como entrada ──────────────
            new(Opcode.HelloAck, OpcodeFamily.ServerOnly, none),
            new(Opcode.AuthResult, OpcodeFamily.ServerOnly, none),
            new(Opcode.Pong, OpcodeFamily.ServerOnly, none),
            new(Opcode.Kick, OpcodeFamily.ServerOnly, none),

            new(Opcode.CharList, OpcodeFamily.ServerOnly, none),
            new(Opcode.CharCreateResult, OpcodeFamily.ServerOnly, none),
            new(Opcode.CharDeleteResult, OpcodeFamily.ServerOnly, none),
            new(Opcode.WorldEnter, OpcodeFamily.ServerOnly, none),

            new(Opcode.EntitySpawn, OpcodeFamily.ServerOnly, none),
            new(Opcode.EntityDespawn, OpcodeFamily.ServerOnly, none),
            new(Opcode.Snapshot, OpcodeFamily.ServerOnly, none),
            new(Opcode.EntityStats, OpcodeFamily.ServerOnly, none),
            new(Opcode.ZoneFlagsUpdate, OpcodeFamily.ServerOnly, none),
            new(Opcode.CombatFlagUpdate, OpcodeFamily.ServerOnly, none),

            new(Opcode.InventoryFull, OpcodeFamily.ServerOnly, none),
            new(Opcode.InventoryDelta, OpcodeFamily.ServerOnly, none),
            new(Opcode.EquipmentUpdate, OpcodeFamily.ServerOnly, none),
            new(Opcode.CurrencyUpdate, OpcodeFamily.ServerOnly, none),

            new(Opcode.ShopData, OpcodeFamily.ServerOnly, none),
            new(Opcode.ShopResult, OpcodeFamily.ServerOnly, none),

            new(Opcode.FarmTileUpdate, OpcodeFamily.ServerOnly, none),

            new(Opcode.CombatEvent, OpcodeFamily.ServerOnly, none),
            new(Opcode.EntityDeath, OpcodeFamily.ServerOnly, none),
            new(Opcode.LootDrop, OpcodeFamily.ServerOnly, none),
            new(Opcode.XpUpdate, OpcodeFamily.ServerOnly, none),

            new(Opcode.ChatMessage, OpcodeFamily.ServerOnly, none),
            new(Opcode.SystemMessage, OpcodeFamily.ServerOnly, none),
        };

        return specs.ToFrozenDictionary(s => s.Opcode);
    }
}
