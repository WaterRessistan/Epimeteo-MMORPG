namespace Epimeteo.Shared.Data;

/// <summary>
/// Qué acción de granja habilita una herramienta puesta en <see cref="EquipSlot.Tool"/>
/// (FASE-08 §2 D4). Sólo hay un hueco de herramienta, así que arar y regar no pueden validarse
/// con "algo hay puesto": <see cref="ItemDefinition.FarmToolAction"/> dice cuál es cuál.
/// </summary>
public enum FarmToolAction : byte
{
    Till = 0,
    Water = 1,
}
