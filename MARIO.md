# MARIO.md — lo que te toca a ti ahora

Esto no es documentación del proyecto (para eso está `CLAUDE.md` y `docs/STATUS.md`, que lee
Claude al principio de cada sesión). Es tu checklist personal para esta ronda: probar de verdad
lo que hasta ahora sólo se ha comprobado por servidor.

## 1. Instala Godot 4.5 — la versión **.NET**

En [godotengine.org/download](https://godotengine.org/download), la build que pone
"**.NET**" (no la estándar) — el cliente está en C#, no en GDScript.

## 2. Clona el repositorio

```
git clone https://github.com/WaterRessistan/Epimeteo-MMORPG.git
```

Ya está actualizado con todo hasta la Fase 15 (primera mitad).

## 3. Abre el proyecto

Abre Godot, "Importar", selecciona `Epimeteo-MMORPG/client/project.godot`.

## 4. Apúntalo al servidor real, no a localhost

Por defecto el cliente intenta conectar a `127.0.0.1` — tu propia máquina, donde no hay nada
corriendo. Tienes que decirle que use el servidor de verdad. Dos formas:

**Desde el editor** (más cómodo): menú **Debug → Customize Run Instances...** → añade como
argumento:
```
--server=wss://epimeteo.waterressistan.duckdns.org/ws
```
y pulsa Play (F5) como siempre.

**Desde terminal**, si lo prefieres:
```
godot --path client -- --server=wss://epimeteo.waterressistan.duckdns.org/ws
```

## 5. Qué probar

Todo esto es funcional pero con **arte placeholder** — no hay packs CC0 reales integrados
todavía, así que espera que se vea muy en bruto. Lo que importa es que funcione, no que sea
bonito.

- [ ] **Conexión**: pantalla de login, ves un RTT en pantalla tras conectar.
- [ ] **Registro y login** de una cuenta.
- [ ] **Crear personaje**: guerrero, mago e híbrido, hasta 5 huecos, borrar uno.
- [ ] **Moverte por el mapa** — con la latencia real de tu conexión, comprueba que no da tirones
      ni "goma elástica" (predicción + reconciliación).
- [ ] **Inventario** (`I`): arrastrar objetos, equipar arma/armadura.
- [ ] **Tienda/armero** (`E`, cerca del NPC): comprar, vender, reparar.
- [ ] **Granja**: arar, plantar, regar, cosechar (el crecimiento real tarda días — no hace falta
      esperar, sólo comprobar que las acciones responden).
- [ ] **Combate** (espacio, en el bosque) — y comprobar que **no** puedes atacar en la plaza
      (zona segura, a propósito).
- [ ] **Habilidades** (`1`-`3`) y **panel de stats** (`K`).
- [ ] **Chat** (`Enter` para escribir, `T` quizá según canal) y comandos `/who`, `/w <nombre> <msj>`.

Si tienes forma de abrir un segundo cliente (otro PC, o dos instancias) a la vez, mejor: así ves
si el movimiento del otro jugador se ve fluido (Fase 4, la más delicada de todo el proyecto).

## 6. Si algo falla

Anota exactamente qué hiciste y qué pasó (o mejor, un pantallazo). Abre una conversación nueva de
Claude Code y dile algo como:

> Lee CLAUDE.md y docs/STATUS.md. Probé el cliente en Godot y esto falló: ...

No hace falta que sea esta misma conversación — `docs/STATUS.md` es justo el mecanismo pensado
para que una sesión nueva recupere el contexto sin que tengas que repetirlo todo.

## 7. Si todo va bien

Igual: nueva conversación, "Lee CLAUDE.md y docs/STATUS.md, probé tal y tal, funciona todo" — y
desde ahí se decide si se sigue con la segunda mitad de la Fase 15 (audio, opciones, build de
distribución) o qué toca.

## 8. Cosas que no son técnicas, pero son tuyas

No urgentes, pero en algún momento hay que decidirlas:

- **Limpiar la base de datos de producción** de las cuentas de prueba (`Bot*`, `smoke_*`,
  `farm_*`, `pvp_*`, `prog_*`, `chat_*`) antes de anunciar el juego de verdad.
- **Conseguir (o pedir explícitamente que se generen) los packs de arte CC0** — la infraestructura
  para integrarlos ya está lista (`client/assets/ATTRIBUTIONS.md`, atlas registry), sólo falta el
  contenido.
