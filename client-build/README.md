# client-build/

Aquí vive la última build exportada del cliente Godot, lista para que `tools/Epimeteo.Launcher`
la descargue vía `/files/` (FASE-15 §2 D1). El contenido no se versiona en git —es salida de
build, no fuente— salvo este `README.md`, que existe para que el mecanismo tenga algo real que
generar, servir y descargar de punta a punta antes de que haya una build de verdad.

En cuanto exista una exportación de Godot (Fase 15, segunda mitad, pendiente de un entorno
gráfico), se copia aquí y se regenera el manifiesto con:

```
dotnet run --project tools/Epimeteo.ReleaseTool -- client-build
```
