using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class MediaConfigParserTests
{
    [Fact]
    public void Parse_empty_string_returns_empty_list()
    {
        var result = MediaConfigParser.Parse("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_whitespace_only_returns_empty_list()
    {
        var result = MediaConfigParser.Parse("   \r\n\t  \n  ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_single_jpg_returns_one_entry()
    {
        var result = MediaConfigParser.Parse("IMG_20260430_123045.jpg");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("IMG_20260430_123045.jpg");
        result[0].Kind.Should().Be(HeyCyanMediaKind.Photo);
        result[0].Size.Should().Be(-1);
    }

    [Fact]
    public void Parse_multiple_files_returns_correct_count()
    {
        var raw = @"IMG_20260430_123045.jpg
VID_20260430_123100.mp4
REC_20260430_123200.opus";

        var result = MediaConfigParser.Parse(raw);

        result.Should().HaveCount(3);
        result[0].Kind.Should().Be(HeyCyanMediaKind.Photo);
        result[1].Kind.Should().Be(HeyCyanMediaKind.Video);
        result[2].Kind.Should().Be(HeyCyanMediaKind.Audio);
    }

    [Fact]
    public void Parse_handles_mixed_line_endings()
    {
        var raw = "IMG_20260430_123045.jpg\nVID_20260430_123100.mp4\r\nREC_20260430_123200.opus";

        var result = MediaConfigParser.Parse(raw);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_ignores_blank_lines()
    {
        var raw = @"IMG_20260430_123045.jpg

VID_20260430_123100.mp4

";

        var result = MediaConfigParser.Parse(raw);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_classifies_extensions_correctly()
    {
        var raw = @"test.jpg
test.jpeg
test.mp4
test.opus
test.txt
test.unknown";

        var result = MediaConfigParser.Parse(raw);

        result.Should().HaveCount(6);
        result[0].Kind.Should().Be(HeyCyanMediaKind.Photo);   // .jpg
        result[1].Kind.Should().Be(HeyCyanMediaKind.Photo);   // .jpeg
        result[2].Kind.Should().Be(HeyCyanMediaKind.Video);   // .mp4
        result[3].Kind.Should().Be(HeyCyanMediaKind.Audio);   // .opus
        result[4].Kind.Should().Be(HeyCyanMediaKind.Other);   // .txt
        result[5].Kind.Should().Be(HeyCyanMediaKind.Other);   // .unknown
    }

    [Fact]
    public void Parse_extracts_timestamp_from_filename()
    {
        var result = MediaConfigParser.Parse("IMG_20260430_143025.jpg");

        result[0].Timestamp.Year.Should().Be(2026);
        result[0].Timestamp.Month.Should().Be(4);
        result[0].Timestamp.Day.Should().Be(30);
        result[0].Timestamp.Hour.Should().Be(14);
        result[0].Timestamp.Minute.Should().Be(30);
        result[0].Timestamp.Second.Should().Be(25);
    }

    [Fact]
    public void Parse_handles_filename_without_timestamp()
    {
        var result = MediaConfigParser.Parse("photo.jpg");

        // Should fall back to current time (within a few seconds)
        result[0].Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_trims_whitespace_from_filenames()
    {
        var raw = "  IMG_20260430_123045.jpg  \n  VID_20260430_123100.mp4  ";

        var result = MediaConfigParser.Parse(raw);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("IMG_20260430_123045.jpg");
        result[1].Name.Should().Be("VID_20260430_123100.mp4");
    }

    [Theory]
    [InlineData("VID_20260430_123100.mp4")]
    [InlineData("REC_20260430_123200.opus")]
    [InlineData("PHOTO_20260430_123300.jpg")]
    public void Parse_handles_various_filename_prefixes(string filename)
    {
        var result = MediaConfigParser.Parse(filename);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be(filename);
        result[0].Timestamp.Year.Should().Be(2026);
        result[0].Timestamp.Month.Should().Be(4);
        result[0].Timestamp.Day.Should().Be(30);
    }

    [Fact]
    public void Parse_handles_invalid_timestamp_gracefully()
    {
        // Invalid date: month 13
        var result = MediaConfigParser.Parse("IMG_20261330_123045.jpg");

        result.Should().HaveCount(1);
        // Should fall back to current time
        result[0].Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
