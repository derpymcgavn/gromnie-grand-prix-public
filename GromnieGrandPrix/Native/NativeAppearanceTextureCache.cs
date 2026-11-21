using Godot;

namespace DerethSurvivors;

// A source image can be reused by many native body-part materials. Only the
// immutable decoded image is shared; every material keeps its own tint/opacity,
// UV scroll and native blending settings. Main-thread use matches DatTextures.
public sealed class NativeAppearanceTextureCache : IDisposable
{
    public const int DefaultMaximumEntries=256;
    public const long DefaultMaximumEstimatedBytes=32L*1024*1024;
    private readonly record struct Key(DatArchive Source,uint Texture,uint Palette,DatArchive PaletteSource,
        bool ClipMap,string ColorSignature,bool PreferEmbeddedPalette);
    private sealed record Entry(Key Key,Texture2D Texture,long EstimatedBytes);
    private readonly Dictionary<Key,LinkedListNode<Entry>> entries=new();
    private readonly LinkedList<Entry> lru=new();
    public int MaximumEntries{get;}
    public long MaximumEstimatedBytes{get;}
    public int Count=>entries.Count;
    public long EstimatedResidentBytes{get;private set;}
    public long Hits{get;private set;}
    public long Misses{get;private set;}
    public long Evictions{get;private set;}

    public NativeAppearanceTextureCache(int maximumEntries=DefaultMaximumEntries,long maximumEstimatedBytes=DefaultMaximumEstimatedBytes)
    {
        if(maximumEntries<1||maximumEstimatedBytes<4)throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        MaximumEntries=maximumEntries;MaximumEstimatedBytes=maximumEstimatedBytes;
    }

    public Texture2D? Load(DatArchive source,uint texture,uint palette,DatArchive paletteArchive,bool clipMap,
        IReadOnlyDictionary<int,uint>? colors,string colorSignature,bool preferEmbeddedPalette=true)
    {
        // Callers already compute this canonical SHA256 signature for native
        // WearMesh keys. Empty/no remaps have identical decoder semantics.
        string signature=colors is null||colors.Count==0?"":colorSignature;
        if(colors is {Count:>0}&&string.IsNullOrEmpty(signature))throw new ArgumentException("Native palette remaps require their canonical color signature.",nameof(colorSignature));
        var key=new Key(source,texture,palette,paletteArchive,clipMap,signature,preferEmbeddedPalette);
        if(entries.TryGetValue(key,out var hit))
        {
            Hits++;lru.Remove(hit);lru.AddLast(hit);return hit.Value.Texture;
        }
        Misses++;
        var decoded=DatTextures.Load(source,texture,palette,paletteArchive,clipMap,colors,preferEmbeddedPalette);
        if(decoded is null)return null;
        // Conservative RGBA8 image size, not a measurement of GPU allocation.
        // Compressed native images can use less; no quality reduction occurs.
        long bytes=checked((long)decoded.GetWidth()*decoded.GetHeight()*4);
        if(bytes>MaximumEstimatedBytes)return decoded;
        while(entries.Count>=MaximumEntries||EstimatedResidentBytes+bytes>MaximumEstimatedBytes)
        {
            var oldest=lru.First!;lru.RemoveFirst();entries.Remove(oldest.Value.Key);
            EstimatedResidentBytes-=oldest.Value.EstimatedBytes;Evictions++;
        }
        var node=lru.AddLast(new Entry(key,decoded,bytes));entries.Add(key,node);EstimatedResidentBytes+=bytes;
        return decoded;
    }

    // Clearing retention never disposes an image referenced by a live native
    // material. Godot's resource reference ownership remains authoritative.
    public void Clear(){entries.Clear();lru.Clear();EstimatedResidentBytes=0;}
    public void Dispose()=>Clear();
}
