using System;
using System.Collections.Generic;
using Godot;

namespace Epimeteo.Client.Net;

/// <summary>
/// Retiene cada frame el tiempo pedido antes de dejarlo pasar, en los dos sentidos. Sirve para
/// jugar en local con la latencia de un servidor real y ver si la predicción y la interpolación
/// aguantan, sin tocar <c>tc netem</c> ni desplegar nada (FASE-04 §7).
/// <para>
/// Apagado por defecto. Se enciende con <c>--lag-ms=150</c> en la línea de órdenes o con la
/// variable de entorno <c>EPIMETEO_LAG_MS</c>. Es una herramienta de desarrollo: con 0 ms el
/// camino es el mismo código, así que no hay una ruta "de verdad" distinta de la que se prueba.
/// </para>
/// </summary>
public sealed class NetLagSimulator
{
    private const string CommandLinePrefix = "--lag-ms=";
    private const string EnvironmentVariable = "EPIMETEO_LAG_MS";

    private readonly Queue<(long DueMs, byte[] Frame)> _queue = new();

    /// <param name="lagMs">Retardo en un sentido, en milisegundos. 0 = sin simulación.</param>
    public NetLagSimulator(int lagMs) => LagMs = Math.Max(0, lagMs);

    /// <summary>Retardo aplicado en cada sentido, en ms.</summary>
    public int LagMs { get; }

    /// <summary>Verdadero si de verdad está reteniendo algo.</summary>
    public bool IsActive => LagMs > 0;

    /// <summary>
    /// Lee el retardo pedido para esta ejecución: primero <c>--lag-ms=N</c>, si no la variable de
    /// entorno, si no 0. Un valor que no sea un número se avisa y se ignora, no se revienta el
    /// arranque por una opción de desarrollo mal escrita.
    /// </summary>
    public static int ReadConfiguredLagMs()
    {
        foreach (var argument in OS.GetCmdlineArgs())
        {
            if (!argument.StartsWith(CommandLinePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var raw = argument[CommandLinePrefix.Length..];
            if (int.TryParse(raw, out var fromCommandLine))
            {
                return fromCommandLine;
            }

            GD.PushWarning($"{CommandLinePrefix} con un valor que no es un número ('{raw}'); se ignora.");
            return 0;
        }

        // System.Environment explícito: Godot.Environment existe y es otra cosa (el entorno de
        // render 3D), así que sin cualificar es ambiguo.
        var fromEnvironment = System.Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrEmpty(fromEnvironment))
        {
            return 0;
        }

        if (int.TryParse(fromEnvironment, out var parsed))
        {
            return parsed;
        }

        GD.PushWarning($"{EnvironmentVariable} con un valor que no es un número ('{fromEnvironment}'); se ignora.");
        return 0;
    }

    /// <summary>Mete un frame en la tubería.</summary>
    public void Push(byte[] frame, long nowMs) => _queue.Enqueue((nowMs + LagMs, frame));

    /// <summary>Saca el siguiente frame que ya ha cumplido su retardo. En orden de llegada.</summary>
    public bool TryPop(long nowMs, out byte[] frame)
    {
        if (_queue.Count > 0 && _queue.Peek().DueMs <= nowMs)
        {
            frame = _queue.Dequeue().Frame;
            return true;
        }

        frame = [];
        return false;
    }

    /// <summary>Tira lo que quede retenido. Se llama al cerrar la conexión.</summary>
    public void Clear() => _queue.Clear();
}
