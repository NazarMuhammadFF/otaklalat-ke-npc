using System.Collections.Generic;
using Godot;

namespace OtakLalat.World;

public enum OdorKind { Food, Poison, Neutral }

/// <summary>Registry objek dunia yang bisa diindera NPC (tanpa query fisika agar murah dan adil untuk kedua metode).</summary>
public static class WorldRegistry
{
    public static readonly List<Edible> Edibles = new();
    public static readonly List<OdorSource> Odors = new();
    public static readonly List<ILooming> Loomers = new();
    public static readonly List<DangerZone> Dangers = new();
    public static Vector3 SunDirection = new(1, 0, 0);   // arah horizontal ke landmark

    public static void Clear()
    {
        Edibles.Clear(); Odors.Clear(); Loomers.Clear(); Dangers.Clear();
    }
}

/// <summary>Objek yang bisa memicu looming (mendekat cepat).</summary>
public interface ILooming
{
    Vector3 LoomPosition { get; }
    float LoomRadius { get; }
}
