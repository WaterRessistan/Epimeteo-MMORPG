# STATUS — Epimeteo MMORPG

**Última actualización:** 2026-08-03 · **Fase actual:** 0 completada → siguiente: **Fase 1**

## Estado

| Área | Estado |
|---|---|
| Diseño y arquitectura | ✅ Cerrado (`docs/00`, `01`, `02`, `03`) |
| Repositorio git | ✅ Inicializado, commit inicial con la Fase 0 |
| Solución .NET | ❌ No existe todavía |
| Servidor | ❌ |
| Cliente Godot | ❌ |
| Base de datos | ❌ (esquema diseñado, sin migraciones) |
| Contenido (`content/`) | ❌ |
| Despliegue | ❌ |

## Entorno

- Desarrollo: WSL2 Ubuntu 24.04 en `/home/mariox/gits/mmorpg`.
- **.NET SDK: no instalado** en WSL. **Godot: no instalado.** Primera tarea de la Fase 1.
- Producción: servidor Ubuntu propio. 80/443/8080 ocupados.
  **Sin confirmar qué proceso tiene el 443** (¿nginx? ¿app Python?). Comprobar en Fase 5 con
  `sudo ss -lntp | grep -E ':(80|443|8080)\b'`.

## Decisiones cerradas en Fase 0

- Nombre: **Epimeteo**. Namespaces `Epimeteo.Shared/.Server/.Client`, solución `Epimeteo.sln`.
- Cliente **Godot 4.5 .NET (C#)**, servidor **.NET 8 headless sin Godot**, librería `Shared`
  común para poder predecir movimiento con el mismo código en ambos lados.
- Transporte **WebSocket binario sobre wss**, detrás de proxy inverso en el 443 existente vía
  subdominio. Servidor en loopback `:5100` (WS) y `:5101` (HTTP).
- **MessagePack** con claves numéricas explícitas. Opcodes nunca reutilizados.
- **PostgreSQL + Dapper**, migraciones SQL con DbUp. Sin EF Core.
- Definiciones de contenido en `content/*.json` versionado en git; la BD sólo guarda estado mutable.
- Tick 20 Hz, snapshots 10 Hz, AOI en celdas de 16×16 tiles, interpolación con 100 ms de buffer.
- **Presentación: tile 16×16 px, resolución base 480×270** (×4 = 1080p exacto), personajes 16×32.
- **PvP por región** (`safe` / `pvp` en el JSON del mapa), no por mapa entero. Pueblo seguro,
  zonas de farmeo hostiles. Sin drop de inventario al morir; penalización de XP. Flag de combate
  de 10 s. Lag compensation con historial de 500 ms.
- **Granja en tiempo real, ~3 días por cultivo.** Job diario a las 05:00 UTC, dos `UPDATE` para
  toda la granja. Regar acelera (+1,0 vs +0,5), nunca mata el cultivo.
- Ciclo día/noche visual **en tiempo real** atado al UTC del servidor: cielo compartido.
- **Fase 14 (escalado multi-proceso) marcada como opcional**: sin objetivo de jugadores, un solo
  proceso con zonas en hilos sobra. Sólo si las métricas de la Fase 13 lo piden.

## Siguiente sesión

**Fase 1 — Esqueleto y primer handshake (Opus).** Detalle en `docs/03-roadmap-fases.md`.
Empieza instalando .NET 8 SDK y Godot 4.5 .NET en WSL.
