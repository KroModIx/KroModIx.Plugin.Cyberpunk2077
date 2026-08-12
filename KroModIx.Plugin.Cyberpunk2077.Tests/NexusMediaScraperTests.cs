using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using KroModIx.Plugin.Cyberpunk2077.Services;
using Xunit;

namespace KroModIx.Plugin.Cyberpunk2077.Tests;

/// <summary>Testet den Regex-Parser des <see cref="NexusMediaScraper"/> mit
/// einem echten Snippet aus dem Cyberpunk-2077-Mod #32512-Media-Tab
/// (staticdelivery-CDN-URL-Muster). Kein Live-HTTP — wir invoken den
/// internen Regex per Reflection.</summary>
public sealed class NexusMediaScraperTests
{
    [Fact]
    public void RegexExtractsThumbnailUrlsFromNexusHtml()
    {
        var html = @"
            <li class='thumb'
                data-src='https://staticdelivery.nexusmods.com/mods/3333/images/thumbnails/32512/32512-1786396670-1550337314.png'
                data-full='https://staticdelivery.nexusmods.com/mods/3333/images/32512/32512-1786396670-1550337314.png'>
            </li>
            <img src=""https://staticdelivery.nexusmods.com/mods/3333/images/thumbnails/32512/32512-1786396674-21826249.png"" />
            <img src=""https://staticdelivery.nexusmods.com/mods/3333/images/thumbnails/32512/32512-1786396674-21826249.png"" />
        ";

        var regexField = typeof(NexusMediaScraper).GetField("ThumbnailUrlRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        regexField.Should().NotBeNull("Regex-Feld muss existieren");
        var regex = (Regex)regexField!.GetValue(null)!;

        var matches = regex.Matches(html);
        var files = matches.Select(m => m.Groups["file"].Value).Distinct().ToList();
        files.Should().HaveCount(2);
        files.Should().Contain("32512-1786396670-1550337314.png");
        files.Should().Contain("32512-1786396674-21826249.png");
    }

    [Fact]
    public void RegexIgnoresNonThumbnailUrls()
    {
        // Der Scraper darf NUR /thumbnails/ matchen — sonst wuerden wir
        // Full-Res-URLs (die auch im HTML stehen) doppelt aufsammeln und
        // beim Umbau auf die Full-URL entstuenden 4K-Bilder im Thumb-Cache.
        var html = @"
            <img src=""https://staticdelivery.nexusmods.com/mods/3333/images/32512/32512-fullres.png"" />
            <img src=""https://staticdelivery.nexusmods.com/mods/3333/images/thumbnails/32512/32512-thumb.png"" />
        ";
        var regexField = typeof(NexusMediaScraper).GetField("ThumbnailUrlRegex",
            BindingFlags.NonPublic | BindingFlags.Static);
        var regex = (Regex)regexField!.GetValue(null)!;
        var matches = regex.Matches(html);
        matches.Count.Should().Be(1);
        matches[0].Groups["file"].Value.Should().Be("32512-thumb.png");
    }
}
