using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using ACE.Server.Command.Handlers;
using ACE.Entity;
using ACE.Entity.Models;
using System.Text.Json;

var socket = new TcpListener(IPAddress.Loopback, 0);
socket.Start();
int port = ((IPEndPoint)socket.LocalEndpoint).Port;
socket.Stop();

using var bridge = new GromnieLookBridge(port);
using var client = new HttpClient();
Uri address = new($"http://127.0.0.1:{port}/rider-appearance");

string replaced = bridge.Publish(1, "old outfit"u8.ToArray());
string valid = bridge.Publish(1, "new outfit"u8.ToArray());
Require((await Redeem(replaced)).StatusCode == HttpStatusCode.Unauthorized);
Require((await Redeem(new string('0', 20))).StatusCode == HttpStatusCode.Unauthorized);
var matches = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Redeem(valid)));
var accepted = matches.Single(response => response.StatusCode == HttpStatusCode.OK);
Require(await accepted.Content.ReadAsStringAsync() == "new outfit");
Require(accepted.Headers.CacheControl?.NoStore == true);
Require(matches.Count(response => response.StatusCode == HttpStatusCode.Unauthorized) == 3);
Require((await client.GetAsync(address)).StatusCode == HttpStatusCode.MethodNotAllowed);
Console.WriteLine("ACE bridge HTTP PASS. Single-use tickets, replacement, concurrency and response boundaries.");
if (args.Length == 1)
{
    var directory = args[0];
    var fixture = Path.Combine(directory, "snapshot.json");
    for (int i = 0; i < 1500 && !File.Exists(fixture); i++)
        await Task.Delay(100);
    using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture));
    var root = doc.RootElement;
    uint Get(string name) => root.GetProperty(name).GetUInt32();
    var obj = new ObjDesc
    {
        PaletteID = Get("Palette"),
        SubPalettes = root.GetProperty("Palettes").EnumerateArray().Select(p => new PropertiesPalette
        {
            SubPaletteId = p.GetProperty("Palette").GetUInt32(),
            Offset = (ushort)(p.GetProperty("Offset").GetInt32() / 8),
            Length = (ushort)(p.GetProperty("Count").GetInt32() == 2048 ? 0 : p.GetProperty("Count").GetInt32() / 8)
        }).ToList(),
        TextureChanges = root.GetProperty("Textures").EnumerateArray().Select(t => new PropertiesTextureMap
        {
            PartIndex = t.GetProperty("Part").GetByte(),
            OldTexture = t.GetProperty("OldTexture").GetUInt32(),
            NewTexture = t.GetProperty("NewTexture").GetUInt32()
        }).ToList(),
        AnimPartChanges = root.GetProperty("Parts").EnumerateArray().Select(p => new PropertiesAnimPart
        {
            Index = p.GetProperty("Part").GetByte(),
            AnimationId = p.GetProperty("Model").GetUInt32()
        }).ToList()
    };
    var code = bridge.Publish(2, root.GetProperty("Name").GetString()!, Get("Setup"), Get("Motion"), Get("Sound"),
        root.GetProperty("Scale").GetSingle(), obj);
    File.WriteAllLines(Path.Combine(directory, "connection.tmp"), [address.AbsoluteUri, code]);
    File.Move(Path.Combine(directory, "connection.tmp"), Path.Combine(directory, "connection.txt"), true);
    for (int i = 0; i < 600 && !File.Exists(Path.Combine(directory, "result.txt")); i++)
        await Task.Delay(100);
    Require(File.Exists(Path.Combine(directory, "result.txt")));
}

async Task<HttpResponseMessage> Redeem(string code)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, address);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", code);
    return await client.SendAsync(request);
}

static void Require(bool passed)
{
    if (!passed) throw new Exception("ACE bridge HTTP assertion failed.");
}

