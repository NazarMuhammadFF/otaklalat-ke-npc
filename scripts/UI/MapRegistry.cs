namespace OtakLalat.UI;

/// <summary>Daftar map uji interaktif (judul tombol, path scene). Urutan = hotkey Ctrl+1..4.</summary>
public static class MapRegistry
{
    public static readonly (string Title, string Path)[] Maps =
    {
        ("Makan vs Racun", "res://scenes/maps/Map_MakanRacun.tscn"),
        ("Bahaya & Belajar", "res://scenes/maps/Map_BahayaBelajar.tscn"),
        ("Koridor Predator", "res://scenes/maps/Map_Predator.tscn"),
        ("Hutan Navigasi", "res://scenes/maps/Map_HutanNavigasi.tscn"),
    };
}
