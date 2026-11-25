using Lightkeeper.Functions.Cards;

namespace Lightkeeper.Functions.Tests.Cards;

public sealed class SolidColorImageTests
{
    private const string Prefix = "data:image/gif;base64,";
    private const int PaletteOffset = 13;

    [Fact]
    public void Data_uri_is_a_one_pixel_gif_whose_palette_holds_the_color()
    {
        var uri = SolidColorImage.DataUri("#4B5563");

        Assert.StartsWith(Prefix, uri);
        var gif = Convert.FromBase64String(uri[Prefix.Length..]);
        Assert.Equal("GIF89a"u8.ToArray(), gif[..6]);
        Assert.Equal(new byte[] { 1, 0, 1, 0 }, gif[6..10]);
        Assert.Equal(new byte[] { 0x4B, 0x55, 0x63 }, gif[PaletteOffset..(PaletteOffset + 3)]);
        Assert.Equal((byte)';', gif[^1]);
    }

    [Theory]
    [InlineData("4B5563")]
    [InlineData("#4B556")]
    [InlineData("#GGGGGG")]
    public void Non_hex_color_is_rejected(string color) => Assert.Throws<ArgumentException>(() => SolidColorImage.DataUri(color));

    [Fact]
    public void Custom_tone_paints_header_and_colors_all_header_text()
    {
        var card = CardStyle.Card(CardStyle.Neutral, "title", "subtitle", [], [], when: ("when", DateTimeOffset.UnixEpoch));

        var header = Assert.IsType<AdaptiveContainer>(card.Body[0]);
        Assert.Equal(SolidColorImage.DataUri(CardStyle.Neutral.BackgroundColor!), header.BackgroundImage?.Url);
        Assert.All(header.Items.Cast<AdaptiveTextBlock>(), t => Assert.Equal(CardStyle.Neutral.TextColor, t.Color));
    }

    [Fact]
    public void Style_only_tone_has_no_background_image()
    {
        var header = Assert.IsType<AdaptiveContainer>(CardStyle.Card(CardStyle.Success, "title", "subtitle", [], []).Body[0]);

        Assert.Null(header.BackgroundImage);
        Assert.Null(((AdaptiveTextBlock)header.Items[1]).Color);
    }
}
