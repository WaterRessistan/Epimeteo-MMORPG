namespace Epimeteo.Server.World;

/// <summary>
/// Un monstruo quiere pegar a alguien. La IA no resuelve el golpe: lo deja aquí y
/// <c>GameWorld</c> lo pasa por <c>CombatSystem</c>, con las mismas validaciones que el ataque de
/// un jugador (FASE-09 §2 D8).
/// </summary>
public readonly record struct PendingMonsterAttack(MonsterEntity Monster, int TargetEntityId);
