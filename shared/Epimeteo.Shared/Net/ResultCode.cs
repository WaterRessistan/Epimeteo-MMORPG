namespace Epimeteo.Shared.Net;

/// <summary>
/// Código de resultado compartido. El servidor <b>nunca</b> manda texto de error al cliente:
/// manda un código y el cliente decide cómo mostrarlo. Permite traducir y no filtra internals.
/// Los valores son estables: no se reordenan ni se reutilizan.
/// </summary>
public enum ResultCode : ushort
{
    Ok = 0,
    UnknownError = 1,
    RateLimited = 2,
    InvalidState = 3,
    VersionMismatch = 4,

    InvalidCredentials = 100,
    AccountBanned = 101,
    AccountAlreadyExists = 102,
    NameTaken = 103,
    NameInvalid = 104,

    SlotOccupied = 200,
    NoCharacterSlots = 201,
    CharacterNotFound = 202,

    InventoryFull = 300,
    ItemNotFound = 301,
    NotEnoughItems = 302,
    NotEquippable = 303,
    WrongClass = 304,
    LevelTooLow = 305,

    NotEnoughGold = 400,
    OutOfStock = 401,
    PriceChanged = 402,
    TooFarAway = 403,
    ShopNotOpen = 404,

    TileOccupied = 500,
    TileNotTilled = 501,
    NotSeeded = 502,
    NotReadyToHarvest = 503,
    WrongSeason = 504,

    TargetNotFound = 600,
    TargetDead = 601,
    OnCooldown = 602,
    NotEnoughMana = 603,
    OutOfRange = 604,
    CannotAttackTarget = 605,
    SafeZone = 606,
    TargetInSafeZone = 607,
    InCombat = 608,
    LevelDifferenceTooHigh = 609,
}
