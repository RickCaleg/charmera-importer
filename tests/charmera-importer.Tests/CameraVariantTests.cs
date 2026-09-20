using charmera_importer.Models;
using charmera_importer.Services;

namespace charmera_importer.Tests;

public class CameraVariantTests
{
    public static TheoryData<string> Ids
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var variant in CameraVariant.All)
            {
                data.Add(variant.Id);
            }

            return data;
        }
    }

    [Fact]
    public void Covers_the_seven_designs_of_the_first_collection()
    {
        Assert.Equal(7, CameraVariant.All.Count);
        Assert.Equal(7, CameraVariant.All.Select(v => v.Id).Distinct().Count());
        Assert.Equal(7, CameraVariant.All.Select(v => v.NameKey).Distinct().Count());
    }

    [Fact]
    public void Ids_round_trip_and_unknown_ids_mean_no_camera()
    {
        foreach (var variant in CameraVariant.All)
        {
            Assert.Same(variant, CameraVariant.FromId(variant.Id));
        }

        Assert.Null(CameraVariant.FromId(null));
        Assert.Null(CameraVariant.FromId("purple"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Text_on_the_accent_stays_readable_in_every_state(string id)
    {
        var p = CameraVariant.FromId(id)!.Palette;
        Assert.True(ThemePalette.Contrast(p.OnAccent, p.Accent) >= 4.5, $"{id}: on accent");
        Assert.True(ThemePalette.Contrast(p.OnAccent, p.AccentHover) >= 4.5, $"{id}: on hover");
        Assert.True(ThemePalette.Contrast(p.OnAccent, p.AccentPressed) >= 4.5, $"{id}: on pressed");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Header_text_stays_readable(string id)
    {
        var p = CameraVariant.FromId(id)!.Palette;
        Assert.True(ThemePalette.Contrast(p.OnHeader, p.Header) >= 4.5, $"{id}: header text");
        Assert.True(ThemePalette.Contrast(p.OnHeaderMuted, p.Header) >= 4.5, $"{id}: muted header text");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_accent_ink_is_visible_on_white_and_on_the_surface(string id)
    {
        var p = CameraVariant.FromId(id)!.Palette;
        Assert.True(ThemePalette.Contrast(p.AccentInk, Avalonia.Media.Colors.White) >= 3.0, $"{id}: ink on white");
        Assert.True(ThemePalette.Contrast(p.AccentInk, p.Surface) >= 2.7, $"{id}: ink on surface");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Primary_text_stays_readable_on_the_tinted_surfaces(string id)
    {
        var p = CameraVariant.FromId(id)!.Palette;
        Assert.True(ThemePalette.Contrast(ThemePalette.Ink, p.Surface) >= 12, $"{id}: surface");
        Assert.True(ThemePalette.Contrast(ThemePalette.Ink, p.Tint) >= 12, $"{id}: tint");
    }

    [Fact]
    public void Classic_is_the_original_look()
    {
        var p = ThemePalette.Classic;
        Assert.Equal(Avalonia.Media.Color.Parse("#ED1C24"), p.Accent);
        Assert.Equal(Avalonia.Media.Color.Parse("#17181B"), p.Header);
        Assert.Empty(p.Stripe);
    }

    [Fact]
    public async Task The_chosen_camera_is_saved_and_old_settings_files_still_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"charmera-settings-{Guid.NewGuid():N}.json");
        try
        {
            var service = new JsonAppSettingsService(path);

            // A file written before cameras existed.
            await File.WriteAllTextAsync(path, """{"LanguageCode":"pt","AppendOriginalFileName":false}""");
            var old = await service.LoadAsync();
            Assert.Null(old.CameraVariantId);
            Assert.False(old.AppendOriginalFileName);

            await service.SaveAsync(old with { CameraVariantId = "blue" });
            Assert.Equal("blue", (await service.LoadAsync()).CameraVariantId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
