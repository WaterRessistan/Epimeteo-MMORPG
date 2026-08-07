namespace Epimeteo.Shared.Net.Messages;

/// <summary>Matices de un golpe, para que el cliente lo pinte distinto. Sólo cosmético.</summary>
[Flags]
public enum CombatEventFlags : byte
{
    None = 0,
    Critical = 1 << 0,
    Dodged = 1 << 1,
    Blocked = 1 << 2,
}
