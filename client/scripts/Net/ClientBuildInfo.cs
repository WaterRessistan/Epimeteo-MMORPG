using System;
using Godot;

namespace Epimeteo.Client.Net;

/// <summary>
/// Datos del build del cliente y del servidor al que conectar.
/// La URL se puede sobrescribir al lanzar el juego para probar contra otro servidor:
/// <code>godot --path client -- --server=wss://midominio/ws</code>
/// </summary>
public static class ClientBuildInfo
{
    /// <summary>Servidor por defecto: el de desarrollo, en loopback.</summary>
    public const string DefaultServerUrl = "ws://127.0.0.1:5100/ws";

    private const string ServerArgPrefix = "--server=";

    /// <summary>Identificador de build que se manda en el <c>Hello</c>. Sólo informativo.</summary>
    public const string Build = "0.1.0-dev";

    /// <summary>URL del servidor: la de <c>--server=</c> si se pasó, si no la de por defecto.</summary>
    public static string ResolveServerUrl()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(ServerArgPrefix, StringComparison.Ordinal))
            {
                return arg[ServerArgPrefix.Length..];
            }
        }

        return DefaultServerUrl;
    }
}
