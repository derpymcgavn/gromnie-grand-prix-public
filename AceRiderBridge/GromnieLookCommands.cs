using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Entity.Actions;
using ACE.Server.Network;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACE.Server.Command.Handlers
{
    public static class GromnieLookCommands
    {
        private static readonly Lazy<GromnieLookBridge> Bridge = new Lazy<GromnieLookBridge>(() =>
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable("GROMNIE_APPEARANCE_PORT"), out int port) || port < 1 || port > 65535)
                throw new InvalidOperationException("Configure GROMNIE_APPEARANCE_PORT to enable the look bridge.");
            return new GromnieLookBridge(port);
        });

        [CommandHandler("gromnielook", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
            "Create a one-time code for your current Gromnie Grand Prix rider appearance.")]
        public static void HandleGromnieLook(Session session, params string[] parameters)
        {
            if (session?.Player is not { } player)
                return;
            var chain = new ActionChain();
            chain.AddAction(player, () =>
            {
                try
                {
                    var bridge = Bridge.Value;
                    string code = bridge.Publish(player.Guid.Full, player.Name, player.SetupTableId,
                        player.MotionTableId, player.SoundTableId, player.ObjScale, player.CalculateObjDesc());
                    player.SendMessage($"Gromnie Grand Prix look code: {code} (one use, expires in 5 minutes).", ChatMessageType.System);
                }
                catch (Exception error) when (error is IOException || error is InvalidOperationException || error is ArgumentException)
                {
                    player.SendMessage("The Gromnie Grand Prix look bridge is unavailable. Try again or ask your shard operator.", ChatMessageType.System);
                    Console.Error.WriteLine($"Gromnie look bridge: {error.Message}");
                }
            });
            chain.EnqueueChain();
        }
    }

    internal sealed class GromnieLookBridge : IDisposable
    {
        private sealed record Ticket(uint Character, DateTimeOffset Expires, byte[] Appearance);
        private readonly Dictionary<string, Ticket> tickets = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private readonly WebApplication app;

        internal GromnieLookBridge(int port)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.Listen(IPAddress.Loopback, port);
                server.AddServerHeader = false;
                server.Limits.MaxRequestBodySize = 0;
            });
            app = builder.Build();
            app.MapPost("/rider-appearance", Redeem);
            app.StartAsync().GetAwaiter().GetResult();
        }

        internal string Publish(uint character, string name, uint setup, uint motion, uint sound, float? scale, ObjDesc look)
        {
            var snapshot = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1,
                Name = name,
                Setup = setup,
                Motion = motion,
                Sound = sound,
                Scale = scale,
                Palette = look.PaletteID,
                Palettes = look.SubPalettes.Select(p => new
                {
                    Palette = p.SubPaletteId,
                    Offset = p.Offset * 8,
                    Count = p.Length == 0 ? 2048 : p.Length * 8
                }),
                Textures = look.TextureChanges.Select(t => new { Part = t.PartIndex, t.OldTexture, t.NewTexture }),
                Parts = look.AnimPartChanges.Select(p => new { Part = p.Index, Model = p.AnimationId })
            });
            return Publish(character, snapshot);
        }

        internal string Publish(uint character, byte[] appearance)
        {
            if (appearance.Length == 0 || appearance.Length > 65_536)
                throw new ArgumentException("The appearance is too large to export.");
            lock (tickets)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var old in tickets.Where(pair => pair.Value.Expires <= now || pair.Value.Character == character).ToArray())
                    tickets.Remove(old.Key);
                if (tickets.Count >= 512)
                    throw new InvalidOperationException("The appearance bridge has reached its active code limit.");
                string code;
                do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)); } while (tickets.ContainsKey(code));
                tickets.Add(code, new Ticket(character, now.AddMinutes(5), appearance));
                return code;
            }
        }

        private IResult Redeem(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                return Results.BadRequest();
            string auth = context.Request.Headers.Authorization.ToString();
            if (auth.Length != 27 || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();
            Ticket ticket;
            lock (tickets)
            {
                if (!tickets.Remove(auth.Substring(7).ToUpperInvariant(), out ticket) || ticket.Expires <= DateTimeOffset.UtcNow)
                    return Results.Unauthorized();
            }
            return Results.Bytes(ticket.Appearance, "application/json");
        }

        public void Dispose() => app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
