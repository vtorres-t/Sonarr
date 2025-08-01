using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class SeasonParserFixture : CoreTest
    {
        [TestCase("30.Series.Season.04.HDTV.XviD-DIMENSION", "30 Series", 4)]
        [TestCase("Sonarr.and.Series.S02.720p.x264-DIMENSION", "Sonarr and Series", 2)]
        [TestCase("The.Series.US.S03.720p.x264-DIMENSION", "The Series US", 3)]
        [TestCase(@"Series.of.Sonarr.S03.720p.BluRay-CLUE\REWARD", "Series of Sonarr", 3)]
        [TestCase("Series Time S02 720p HDTV x264 CRON", "Series Time", 2)]
        [TestCase("Series.2021.S04.iNTERNAL.DVDRip.XviD-VCDVaULT", "Series 2021", 4)]
        [TestCase("Series Five 0 S01 720p WEB DL DD5 1 H 264 NT", "Series Five 0", 1)]
        [TestCase("30 Series S03 WS PDTV XviD FUtV", "30 Series", 3)]
        [TestCase("The Series Season 4 WS PDTV XviD FUtV", "The Series", 4)]
        [TestCase("Series Season 1 720p WEB DL DD 5 1 h264 TjHD", "Series", 1)]
        [TestCase("The Series Season4 WS PDTV XviD FUtV", "The Series", 4)]
        [TestCase("Series S 01 720p WEB DL DD 5 1 h264 TjHD", "Series", 1)]
        [TestCase("Series Confidential   Season 3", "Series Confidential", 3)]
        [TestCase("Series.S01.720p.WEBDL.DD5.1.H.264-NTb", "Series", 1)]
        [TestCase("Series.Makes.It.Right.S02.720p.HDTV.AAC5.1.x265-NOGRP", "Series Makes It Right", 2)]
        [TestCase("My.Series.S2014.720p.HDTV.x264-ME", "My Series", 2014)]
        [TestCase("Series.Saison3.VOSTFR.HDTV.XviD-NOTAG", "Series", 3)]
        [TestCase("Series.SAISON.1.VFQ.PDTV.H264-ACC-ROLLED", "Series", 1)]
        [TestCase("Series Title - Series 1 (1970) DivX", "Series Title", 1)]
        [TestCase("SeriesTitle.S03.540p.AMZN.WEB-DL.DD+2.0.x264-RTN", "SeriesTitle", 3)]
        [TestCase("Series.Title.S01.576p.BluRay.DD5.1.x264-HiSD", "Series Title", 1)]
        [TestCase("Series.Stagione.3.HDTV.XviD-NOTAG", "Series", 3)]
        [TestCase("Series.Stagione.3.HDTV.XviD-NOTAG", "Series", 3)]
        [TestCase("Series No More S01 2023 1080p WEB-DL AVC AC3 2.0 Dual Audio -ZR-", "Series No More", 1)]
        [TestCase("Series Title / S1E1-8 of 8 [2024, WEB-DL 1080p] + Original + RUS", "Series Title", 1)]
        [TestCase("Series Title / S2E1-16 of 16 [2022, WEB-DL] RUS", "Series Title", 2)]
        [TestCase("[hchcsen] Mobile Series 00 S01 [BD Remux Dual Audio 1080p AVC 2xFLAC] (Kidou Senshi Gundam 00 Season 1)", "Mobile Series 00", 1)]
        [TestCase("[HorribleRips] Mobile Series 00 S1 [1080p]", "Mobile Series 00", 1)]
        [TestCase("[Zoombie] Series 100: Bucket List S01 [Web][MKV][h265 10-bit][1080p][AC3 2.0][Softsubs (Zoombie)]", "Series 100: Bucket List", 1)]
        [TestCase("[GROUP] Series: Title (2023) (Season 1) [BDRip] [1080p Dual Audio HEVC 10 bits DDP] (serie) (Batch)", "Series: Title (2023)", 1)]
        [TestCase("[GROUP] Series: Title (2023) (Season 1) [BDRip] [1080p Dual Audio HEVC 10-bits DDP] (serie) (Batch)", "Series: Title (2023)", 1)]
        [TestCase("[GROUP] Series: Title (2023) (Season 1) [BDRip] [1080p Dual Audio HEVC 10-bit DDP] (serie) (Batch)", "Series: Title (2023)", 1)]
        [TestCase("Seriesless (2016/S01/WEB-DL/1080p/AC3 5.1/DUAL/SUB)", "Seriesless (2016)", 1)]
        public void should_parse_full_season_release(string postTitle, string title, int season)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.SeasonNumber.Should().Be(season);
            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().BeEmpty();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeTrue();
        }

        [TestCase("Acropolis Series S05 EXTRAS DVDRip XviD RUNNER", "Acropolis Series", 5)]
        [TestCase("Punky Series S01 EXTRAS DVDRip XviD RUNNER", "Punky Series", 1)]
        [TestCase("Instant Series S03 EXTRAS DVDRip XviD OSiTV", "Instant Series", 3)]
        [TestCase("The.Series.S03.Extras.01.Deleted.Scenes.720p", "The Series", 3)]
        [TestCase("The.Series.S03.Extras.02.720p", "The Series", 3)]
        public void should_parse_season_extras(string postTitle, string title, int season)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.SeasonNumber.Should().Be(season);
            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().BeEmpty();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeTrue();
            result.IsSeasonExtra.Should().BeTrue();
        }

        [TestCase("Series.to.Me.S03.SUBPACK.DVDRip.XviD-REWARD", "Series to Me", 3)]
        [TestCase("The.Series.S02.SUBPACK.DVDRip.XviD-REWARD", "The Series", 2)]
        [TestCase("Series.S11.SUBPACK.DVDRip.XviD-REWARD", "Series", 11)]
        public void should_parse_season_subpack(string postTitle, string title, int season)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.SeasonNumber.Should().Be(season);
            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().BeEmpty();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeTrue();
            result.IsSeasonExtra.Should().BeTrue();
        }

        [TestCase("The.Series.2016.S02.Part.1.1080p.NF.WEBRip.DD5.1.x264-NTb", "The Series 2016", 2, 1)]
        [TestCase("The.Series.S07.Vol.1.1080p.NF.WEBRip.DD5.1.x264-NTb", "The Series", 7, 1)]
        [TestCase("The.Series.S06.P1.1080p.Blu-Ray.10-Bit.Dual-Audio.TrueHD.x265-iAHD", "The Series", 6, 1)]
        public void should_parse_partial_season_release(string postTitle, string title, int season, int seasonPart)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.SeasonNumber.Should().Be(season);
            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().BeEmpty();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();
            result.IsPartialSeason.Should().BeTrue();
            result.SeasonPart.Should().Be(seasonPart);
        }

        [TestCase("The Series S01-05 WS BDRip X264-REWARD-No Rars", "The Series", 1)]
        [TestCase("Series.Title.S01-S09.1080p.AMZN.WEB-DL.DDP2.0.H.264-NTb", "Series Title", 1)]
        [TestCase("Series Title S01 - S07 BluRay 1080p x264 REPACK -SacReD", "Series Title", 1)]
        [TestCase("Series Title Season 01-07 BluRay 1080p x264 REPACK -SacReD", "Series Title", 1)]
        [TestCase("Series Title Season 01 - Season 07 BluRay 1080p x264 REPACK -SacReD", "Series Title", 1)]
        [TestCase("Series Title Complete Series S01 S04 (1080p BluRay x265 HEVC 10bit AAC 5.1 Vyndros)", "Series Title", 1)]
        [TestCase("Series Title S01 S04 (1080p BluRay x265 HEVC 10bit AAC 5.1 Vyndros)", "Series Title", 1)]
        [TestCase("Series Title S01 04 (1080p BluRay x265 HEVC 10bit AAC 5.1 Vyndros)", "Series Title", 1)]
        public void should_parse_multi_season_release(string postTitle, string title, int firstSeason)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.SeasonNumber.Should().Be(firstSeason);
            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().BeEmpty();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeTrue();
            result.IsPartialSeason.Should().BeFalse();
            result.IsMultiSeason.Should().BeTrue();
        }

        [Test]
        public void should_not_parse_season_folders()
        {
            var result = Parser.Parser.ParseTitle("Season 3");
            result.Should().BeNull();
        }
    }
}
