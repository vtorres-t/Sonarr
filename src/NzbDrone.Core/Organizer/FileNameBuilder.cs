using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Diacritical;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Organizer
{
    public interface IBuildFileNames
    {
        string BuildFileName(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension = "", NamingConfig namingConfig = null, List<CustomFormat> customFormats = null);
        string BuildFilePath(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension, NamingConfig namingConfig = null, List<CustomFormat> customFormats = null);
        string BuildSeasonPath(Series series, int seasonNumber);
        string GetSeriesFolder(Series series, NamingConfig namingConfig = null);
        string GetSeasonFolder(Series series, int seasonNumber, NamingConfig namingConfig = null);
        bool RequiresEpisodeTitle(Series series, List<Episode> episodes);
        bool RequiresAbsoluteEpisodeNumber();
    }

    public class FileNameBuilder : IBuildFileNames
    {
        private const string MediaInfoVideoDynamicRangeToken = "{MediaInfo VideoDynamicRange}";
        private const string MediaInfoVideoDynamicRangeTypeToken = "{MediaInfo VideoDynamicRangeType}";

        private readonly INamingConfigService _namingConfigService;
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly IUpdateMediaInfo _mediaInfoUpdater;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly ICached<EpisodeFormat[]> _episodeFormatCache;
        private readonly ICached<AbsoluteEpisodeFormat[]> _absoluteEpisodeFormatCache;
        private readonly ICached<bool> _requiresEpisodeTitleCache;
        private readonly ICached<bool> _requiresAbsoluteEpisodeNumberCache;
        private readonly ICached<bool> _patternHasEpisodeIdentifierCache;
        private readonly Logger _logger;

        private static readonly Regex TitleRegex = new Regex(@"(?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>(?:[a-z0-9]+)(?:(?<separator>[- ._]+)(?:[a-z0-9]+))?)(?::(?<customFormat>[ ,a-z0-9+-]+(?<![- ])))?(?<suffix>[- ._)\]]*)\}",
                                                             RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static readonly Regex EpisodeRegex = new Regex(@"(?<episode>\{episode(?:\:0+)?})",
                                                               RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SeasonRegex = new Regex(@"(?<season>\{season(?:\:0+)?})",
                                                              RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AbsoluteEpisodeRegex = new Regex(@"(?<absolute>\{absolute(?:\:0+)?})",
                                                               RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SeasonEpisodePatternRegex = new Regex(@"(?<separator>(?<=})[- ._]+?)?(?<seasonEpisode>s?{season(?:\:0+)?}(?<episodeSeparator>[- ._]?[ex])(?<episode>{episode(?:\:0+)?}))(?<separator>[- ._]+?(?={))?",
                                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex AbsoluteEpisodePatternRegex = new Regex(@"(?<separator>(?<=})[- ._]+?)?(?<absolute>{absolute(?:\:0+)?})(?<separator>[- ._]+?(?={))?",
                                                                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex AirDateRegex = new Regex(@"\{Air(\s|\W|_)Date\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SeriesTitleRegex = new Regex(@"(?<token>\{(?:Series)(?<separator>[- ._])(Clean)?Title(The)?(Without)?(Year)?(?::(?<customFormat>[0-9-]+))?\})",
                                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FileNameCleanupRegex = new Regex(@"([- ._])(\1)+", RegexOptions.Compiled);
        private static readonly Regex TrimSeparatorsRegex = new Regex(@"[- ._]+$", RegexOptions.Compiled);

        private static readonly Regex ScenifyRemoveChars = new Regex(@"(?<=\s)(,|<|>|\/|\\|;|:|'|""|\||`|’|~|!|\?|@|$|%|^|\*|-|_|=){1}(?=\s)|('|`|’|:|\?|,)(?=(?:(?:s|m|t|ve|ll|d|re)\s)|\s|$)|(\(|\)|\[|\]|\{|\})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ScenifyReplaceChars = new Regex(@"[\/]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // TODO: Support Written numbers (One, Two, etc) and Roman Numerals (I, II, III etc)
        private static readonly Regex MultiPartCleanupRegex = new Regex(@"(?:\:?\s?(?:\(\d+\)|(Part|Pt\.?)\s?\d+))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly char[] EpisodeTitleTrimCharacters = new[] { ' ', '.', '?' };

        private static readonly Regex TitlePrefixRegex = new Regex(@"^(The|An|A) (.*?)((?: *\([^)]+\))*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex YearRegex = new Regex(@"\(\d{4}\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ReservedDeviceNamesRegex = new Regex(@"^(?:aux|com[1-9]|con|lpt[1-9]|nul|prn)\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // generated from https://www.loc.gov/standards/iso639-2/ISO-639-2_utf-8.txt
        public static readonly ImmutableDictionary<string, string> Iso639BTMap = new Dictionary<string, string>
        {
            { "alb", "sqi" },
            { "arm", "hye" },
            { "baq", "eus" },
            { "bur", "mya" },
            { "chi", "zho" },
            { "cze", "ces" },
            { "dut", "nld" },
            { "fre", "fra" },
            { "geo", "kat" },
            { "ger", "deu" },
            { "gre", "ell" },
            { "gsw", "deu" },
            { "ice", "isl" },
            { "mac", "mkd" },
            { "mao", "mri" },
            { "may", "msa" },
            { "per", "fas" },
            { "rum", "ron" },
            { "slo", "slk" },
            { "tib", "bod" },
            { "wel", "cym" }
        }.ToImmutableDictionary();

        public static readonly ImmutableArray<string> BadCharacters = ImmutableArray.Create("\\", "/", "<", ">", "?", "*", "|", "\"");
        public static readonly ImmutableArray<string> GoodCharacters = ImmutableArray.Create("+", "+", "", "", "!", "-", "", "");

        public FileNameBuilder(INamingConfigService namingConfigService,
                               IQualityDefinitionService qualityDefinitionService,
                               ICacheManager cacheManager,
                               IUpdateMediaInfo mediaInfoUpdater,
                               ICustomFormatCalculationService formatCalculator,
                               Logger logger)
        {
            _namingConfigService = namingConfigService;
            _qualityDefinitionService = qualityDefinitionService;
            _mediaInfoUpdater = mediaInfoUpdater;
            _formatCalculator = formatCalculator;
            _episodeFormatCache = cacheManager.GetCache<EpisodeFormat[]>(GetType(), "episodeFormat");
            _absoluteEpisodeFormatCache = cacheManager.GetCache<AbsoluteEpisodeFormat[]>(GetType(), "absoluteEpisodeFormat");
            _requiresEpisodeTitleCache = cacheManager.GetCache<bool>(GetType(), "requiresEpisodeTitle");
            _requiresAbsoluteEpisodeNumberCache = cacheManager.GetCache<bool>(GetType(), "requiresAbsoluteEpisodeNumber");
            _patternHasEpisodeIdentifierCache = cacheManager.GetCache<bool>(GetType(), "patternHasEpisodeIdentifier");
            _logger = logger;
        }

        private string BuildFileName(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension, int maxPath, NamingConfig namingConfig = null, List<CustomFormat> customFormats = null)
        {
            if (namingConfig == null)
            {
                namingConfig = _namingConfigService.GetConfig();
            }

            if (!namingConfig.RenameEpisodes)
            {
                return GetOriginalTitle(episodeFile, true) + extension;
            }

            if (namingConfig.StandardEpisodeFormat.IsNullOrWhiteSpace() && series.SeriesType == SeriesTypes.Standard)
            {
                throw new NamingFormatException("Standard episode format cannot be empty");
            }

            if (namingConfig.DailyEpisodeFormat.IsNullOrWhiteSpace() && series.SeriesType == SeriesTypes.Daily)
            {
                throw new NamingFormatException("Daily episode format cannot be empty");
            }

            if (namingConfig.AnimeEpisodeFormat.IsNullOrWhiteSpace() && series.SeriesType == SeriesTypes.Anime)
            {
                throw new NamingFormatException("Anime episode format cannot be empty");
            }

            var pattern = namingConfig.StandardEpisodeFormat;

            episodes = episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();

            if (series.SeriesType == SeriesTypes.Daily && episodes.First().SeasonNumber > 0)
            {
                pattern = namingConfig.DailyEpisodeFormat;
            }

            if (series.SeriesType == SeriesTypes.Anime &&
                (episodes.All(e => e.AbsoluteEpisodeNumber.HasValue) ||
                !RequiresAbsoluteEpisodeNumber()))
            {
                pattern = namingConfig.AnimeEpisodeFormat;
            }

            var splitPatterns = pattern.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var components = new List<string>();

            for (var i = 0; i < splitPatterns.Length; i++)
            {
                var splitPattern = splitPatterns[i];
                var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);
                var patternHasEpisodeIdentifier = GetPatternHasEpisodeIdentifier(splitPattern);

                splitPattern = AddSeasonEpisodeNumberingTokens(splitPattern, tokenHandlers, episodes, namingConfig);
                splitPattern = AddAbsoluteNumberingTokens(splitPattern, tokenHandlers, series, episodes, namingConfig);
                splitPattern = splitPattern.Replace("...", "{{ellipsis}}");

                UpdateMediaInfoIfNeeded(splitPattern, episodeFile, series);

                AddSeriesTokens(tokenHandlers, series);
                AddIdTokens(tokenHandlers, series);
                AddEpisodeTokens(tokenHandlers, episodes);
                AddEpisodeTitlePlaceholderTokens(tokenHandlers);
                AddEpisodeFileTokens(tokenHandlers, episodeFile, !patternHasEpisodeIdentifier || episodeFile.Id == 0);
                AddQualityTokens(tokenHandlers, series, episodeFile);
                AddMediaInfoTokens(tokenHandlers, episodeFile);
                AddCustomFormats(tokenHandlers, series, episodeFile, customFormats);

                var component = ReplaceTokens(splitPattern, tokenHandlers, namingConfig, true).Trim();
                var maxPathSegmentLength = Math.Min(LongPathSupport.MaxFileNameLength, maxPath);
                if (i == splitPatterns.Length - 1)
                {
                    maxPathSegmentLength -= extension.GetByteCount();
                }

                var maxEpisodeTitleLength = maxPathSegmentLength - GetLengthWithoutEpisodeTitle(component, namingConfig);

                AddEpisodeTitleTokens(tokenHandlers, episodes, maxEpisodeTitleLength);
                component = ReplaceTokens(component, tokenHandlers, namingConfig).Trim();

                component = FileNameCleanupRegex.Replace(component, match => match.Captures[0].Value[0].ToString());
                component = TrimSeparatorsRegex.Replace(component, string.Empty);
                component = component.Replace("{ellipsis}", "...");
                component = ReplaceReservedDeviceNames(component);

                components.Add(component);
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), components) + extension;
        }

        public string BuildFileName(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension = "", NamingConfig namingConfig = null, List<CustomFormat> customFormats = null)
        {
            return BuildFileName(episodes, series, episodeFile, extension, LongPathSupport.MaxFilePathLength, namingConfig, customFormats);
        }

        public string BuildFilePath(List<Episode> episodes, Series series, EpisodeFile episodeFile, string extension, NamingConfig namingConfig = null, List<CustomFormat> customFormats = null)
        {
            Ensure.That(extension, () => extension).IsNotNullOrWhiteSpace();

            var seasonPath = BuildSeasonPath(series, episodes.First().SeasonNumber);
            var remainingPathLength = LongPathSupport.MaxFilePathLength - seasonPath.GetByteCount() - 1;
            var fileName = BuildFileName(episodes, series, episodeFile, extension, remainingPathLength, namingConfig, customFormats);

            return Path.Combine(seasonPath, fileName);
        }

        public string BuildSeasonPath(Series series, int seasonNumber)
        {
            var path = series.Path;

            if (series.SeasonFolder)
            {
                var seasonFolder = GetSeasonFolder(series, seasonNumber);

                seasonFolder = CleanFileName(seasonFolder);

                path = Path.Combine(path, seasonFolder);
            }

            return path;
        }

        public string GetSeriesFolder(Series series, NamingConfig namingConfig = null)
        {
            if (namingConfig == null)
            {
                namingConfig = _namingConfigService.GetConfig();
            }

            var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);

            AddSeriesTokens(tokenHandlers, series);
            AddIdTokens(tokenHandlers, series);

            var folderName = ReplaceTokens(namingConfig.SeriesFolderFormat, tokenHandlers, namingConfig);

            folderName = CleanFolderName(folderName);
            folderName = ReplaceReservedDeviceNames(folderName);
            folderName = folderName.Replace("{ellipsis}", "...");

            return folderName;
        }

        public string GetSeasonFolder(Series series, int seasonNumber, NamingConfig namingConfig = null)
        {
            if (namingConfig == null)
            {
                namingConfig = _namingConfigService.GetConfig();
            }

            var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);

            AddSeriesTokens(tokenHandlers, series);
            AddIdTokens(tokenHandlers, series);
            AddSeasonTokens(tokenHandlers, seasonNumber);

            var format = seasonNumber == 0 ? namingConfig.SpecialsFolderFormat : namingConfig.SeasonFolderFormat;
            var folderName = ReplaceTokens(format, tokenHandlers, namingConfig);

            folderName = CleanFolderName(folderName);
            folderName = ReplaceReservedDeviceNames(folderName);
            folderName = folderName.Replace("{ellipsis}", "...");

            return folderName;
        }

        public static string CleanTitle(string title)
        {
            title = title.Replace("&", "and");
            title = ScenifyReplaceChars.Replace(title, " ");
            title = ScenifyRemoveChars.Replace(title, string.Empty);

            return title.RemoveDiacritics();
        }

        public static string TitleThe(string title)
        {
            return TitlePrefixRegex.Replace(title, "$2, $1$3");
        }

        public static string CleanTitleThe(string title)
        {
            if (TitlePrefixRegex.IsMatch(title))
            {
                var splitResult = TitlePrefixRegex.Split(title);
                return $"{CleanTitle(splitResult[2]).Trim()}, {splitResult[1]}{CleanTitle(splitResult[3])}";
            }

            return CleanTitle(title);
        }

        public static string TitleYear(string title, int year)
        {
            // Don't use 0 for the year.
            if (year == 0)
            {
                return title;
            }

            // Regex match in case the year in the title doesn't match the year, for whatever reason.
            if (YearRegex.IsMatch(title))
            {
                return title;
            }

            return $"{title} ({year})";
        }

        public static string CleanTitleTheYear(string title, int year)
        {
            // Don't use 0 for the year.
            if (year == 0)
            {
                return CleanTitleThe(title);
            }

            // Regex match in case the year in the title doesn't match the year, for whatever reason.
            if (YearRegex.IsMatch(title))
            {
                var splitReturn = YearRegex.Split(title);
                var yearMatch = YearRegex.Match(title);
                return $"{CleanTitleThe(splitReturn[0].Trim())} {yearMatch.Value[1..5]}";
            }

            return $"{CleanTitleThe(title)} {year}";
        }

        public static string TitleWithoutYear(string title)
        {
            title = YearRegex.Replace(title, "");

            return title;
        }

        public static string TitleFirstCharacter(string title)
        {
            if (char.IsLetterOrDigit(title[0]))
            {
                return title.Substring(0, 1).ToUpper().RemoveDiacritics()[0].ToString();
            }

            // Try the second character if the first was non alphanumeric
            if (char.IsLetterOrDigit(title[1]))
            {
                return title.Substring(1, 1).ToUpper().RemoveDiacritics()[0].ToString();
            }

            // Default to "_" if no alphanumeric character can be found in the first 2 positions
            return "_";
        }

        public static string CleanFileName(string name)
        {
            return CleanFileName(name, NamingConfig.Default);
        }

        public static string CleanFolderName(string name)
        {
            name = FileNameCleanupRegex.Replace(name, match => match.Captures[0].Value[0].ToString());

            return name.Trim(' ', '.');
        }

        public bool RequiresEpisodeTitle(Series series, List<Episode> episodes)
        {
            var namingConfig = _namingConfigService.GetConfig();
            var pattern = namingConfig.StandardEpisodeFormat;

            if (!namingConfig.RenameEpisodes)
            {
                return false;
            }

            if (series.SeriesType == SeriesTypes.Daily)
            {
                pattern = namingConfig.DailyEpisodeFormat;
            }

            if (series.SeriesType == SeriesTypes.Anime && episodes.All(e => e.AbsoluteEpisodeNumber.HasValue))
            {
                pattern = namingConfig.AnimeEpisodeFormat;
            }

            return _requiresEpisodeTitleCache.Get(pattern, () =>
            {
                var matches = TitleRegex.Matches(pattern);

                foreach (Match match in matches)
                {
                    var token = match.Groups["token"].Value;

                    if (FileNameBuilderTokenEqualityComparer.Instance.Equals(token, "{Episode Title}") ||
                        FileNameBuilderTokenEqualityComparer.Instance.Equals(token, "{Episode CleanTitle}"))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        public bool RequiresAbsoluteEpisodeNumber()
        {
            var namingConfig = _namingConfigService.GetConfig();
            var pattern = namingConfig.AnimeEpisodeFormat;

            return _requiresAbsoluteEpisodeNumberCache.Get(pattern, () =>
            {
                var matches = AbsoluteEpisodeRegex.Matches(pattern);

                return matches.Count > 0;
            });
        }

        private void AddSeriesTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Series series)
        {
            tokenHandlers["{Series Title}"] = m => Truncate(series.Title, m.CustomFormat);
            tokenHandlers["{Series CleanTitle}"] = m => Truncate(CleanTitle(series.Title), m.CustomFormat);
            tokenHandlers["{Series TitleYear}"] = m => Truncate(TitleYear(series.Title, series.Year), m.CustomFormat);
            tokenHandlers["{Series CleanTitleYear}"] = m => Truncate(CleanTitle(TitleYear(series.Title, series.Year)), m.CustomFormat);
            tokenHandlers["{Series TitleWithoutYear}"] = m => Truncate(TitleWithoutYear(series.Title), m.CustomFormat);
            tokenHandlers["{Series CleanTitleWithoutYear}"] = m => Truncate(CleanTitle(TitleWithoutYear(series.Title)), m.CustomFormat);
            tokenHandlers["{Series TitleThe}"] = m => Truncate(TitleThe(series.Title), m.CustomFormat);
            tokenHandlers["{Series CleanTitleThe}"] = m => Truncate(CleanTitleThe(series.Title), m.CustomFormat);
            tokenHandlers["{Series TitleTheYear}"] = m => Truncate(TitleYear(TitleThe(series.Title), series.Year), m.CustomFormat);
            tokenHandlers["{Series CleanTitleTheYear}"] = m => Truncate(CleanTitleTheYear(series.Title, series.Year), m.CustomFormat);
            tokenHandlers["{Series TitleTheWithoutYear}"] = m => Truncate(TitleWithoutYear(TitleThe(series.Title)), m.CustomFormat);
            tokenHandlers["{Series CleanTitleTheWithoutYear}"] = m => Truncate(CleanTitleThe(TitleWithoutYear(series.Title)), m.CustomFormat);
            tokenHandlers["{Series TitleFirstCharacter}"] = m => Truncate(TitleFirstCharacter(TitleThe(series.Title)), m.CustomFormat);
            tokenHandlers["{Series Year}"] = m => series.Year.ToString();
        }

        private string AddSeasonEpisodeNumberingTokens(string pattern, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, List<Episode> episodes, NamingConfig namingConfig)
        {
            var episodeFormats = GetEpisodeFormat(pattern).DistinctBy(v => v.SeasonEpisodePattern).ToList();

            var index = 1;
            foreach (var episodeFormat in episodeFormats)
            {
                var seasonEpisodePattern = episodeFormat.SeasonEpisodePattern;
                string formatPattern;

                switch ((MultiEpisodeStyle)namingConfig.MultiEpisodeStyle)
                {
                    case MultiEpisodeStyle.Duplicate:
                        formatPattern = episodeFormat.Separator + episodeFormat.SeasonEpisodePattern;
                        seasonEpisodePattern = FormatNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Repeat:
                        formatPattern = episodeFormat.EpisodeSeparator + episodeFormat.EpisodePattern;
                        seasonEpisodePattern = FormatNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Scene:
                        formatPattern = "-" + episodeFormat.EpisodeSeparator + episodeFormat.EpisodePattern;
                        seasonEpisodePattern = FormatNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Range:
                        formatPattern = "-" + episodeFormat.EpisodePattern;
                        seasonEpisodePattern = FormatRangeNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.PrefixedRange:
                        formatPattern = "-" + episodeFormat.EpisodeSeparator + episodeFormat.EpisodePattern;
                        seasonEpisodePattern = FormatRangeNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;

                    // MultiEpisodeStyle.Extend
                    default:
                        formatPattern = "-" + episodeFormat.EpisodePattern;
                        seasonEpisodePattern = FormatNumberTokens(seasonEpisodePattern, formatPattern, episodes);
                        break;
                }

                var token = string.Format("{{Season Episode{0}}}", index++);
                pattern = pattern.Replace(episodeFormat.SeasonEpisodePattern, token);
                tokenHandlers[token] = m => seasonEpisodePattern;
            }

            AddSeasonTokens(tokenHandlers, episodes.First().SeasonNumber);

            if (episodes.Count > 1)
            {
                tokenHandlers["{Episode}"] = m => episodes.First().EpisodeNumber.ToString(m.CustomFormat) + "-" + episodes.Last().EpisodeNumber.ToString(m.CustomFormat);
            }
            else
            {
                tokenHandlers["{Episode}"] = m => episodes.First().EpisodeNumber.ToString(m.CustomFormat);
            }

            return pattern;
        }

        private string AddAbsoluteNumberingTokens(string pattern, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Series series, List<Episode> episodes, NamingConfig namingConfig)
        {
            var absoluteEpisodeFormats = GetAbsoluteFormat(pattern).DistinctBy(v => v.AbsoluteEpisodePattern).ToList();

            var index = 1;
            foreach (var absoluteEpisodeFormat in absoluteEpisodeFormats)
            {
                if (series.SeriesType != SeriesTypes.Anime || episodes.Any(e => !e.AbsoluteEpisodeNumber.HasValue))
                {
                    pattern = pattern.Replace(absoluteEpisodeFormat.AbsoluteEpisodePattern, "");
                    continue;
                }

                var absoluteEpisodePattern = absoluteEpisodeFormat.AbsoluteEpisodePattern;
                string formatPattern;

                switch ((MultiEpisodeStyle)namingConfig.MultiEpisodeStyle)
                {
                    case MultiEpisodeStyle.Duplicate:
                        formatPattern = absoluteEpisodeFormat.Separator + absoluteEpisodeFormat.AbsoluteEpisodePattern;
                        absoluteEpisodePattern = FormatAbsoluteNumberTokens(absoluteEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Repeat:
                        var repeatSeparator = absoluteEpisodeFormat.Separator.Trim().IsNullOrWhiteSpace() ? " " : absoluteEpisodeFormat.Separator.Trim();

                        formatPattern = repeatSeparator + absoluteEpisodeFormat.AbsoluteEpisodePattern;
                        absoluteEpisodePattern = FormatAbsoluteNumberTokens(absoluteEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Scene:
                        formatPattern = "-" + absoluteEpisodeFormat.AbsoluteEpisodePattern;
                        absoluteEpisodePattern = FormatAbsoluteNumberTokens(absoluteEpisodePattern, formatPattern, episodes);
                        break;

                    case MultiEpisodeStyle.Range:
                    case MultiEpisodeStyle.PrefixedRange:
                        formatPattern = "-" + absoluteEpisodeFormat.AbsoluteEpisodePattern;
                        var eps = new List<Episode> { episodes.First() };

                        if (episodes.Count > 1)
                        {
                            eps.Add(episodes.Last());
                        }

                        absoluteEpisodePattern = FormatAbsoluteNumberTokens(absoluteEpisodePattern, formatPattern, eps);
                        break;

                        // MultiEpisodeStyle.Extend
                    default:
                        formatPattern = "-" + absoluteEpisodeFormat.AbsoluteEpisodePattern;
                        absoluteEpisodePattern = FormatAbsoluteNumberTokens(absoluteEpisodePattern, formatPattern, episodes);
                        break;
                }

                var token = string.Format("{{Absolute Pattern{0}}}", index++);
                pattern = pattern.Replace(absoluteEpisodeFormat.AbsoluteEpisodePattern, token);
                tokenHandlers[token] = m => absoluteEpisodePattern;
            }

            return pattern;
        }

        private void AddSeasonTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, int seasonNumber)
        {
            tokenHandlers["{Season}"] = m => seasonNumber.ToString(m.CustomFormat);
        }

        private void AddEpisodeTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, List<Episode> episodes)
        {
            if (!episodes.First().AirDate.IsNullOrWhiteSpace())
            {
                tokenHandlers["{Air Date}"] = m => episodes.First().AirDate.Replace('-', ' ');
            }
            else
            {
                tokenHandlers["{Air Date}"] = m => "Unknown";
            }
        }

        private void AddEpisodeTitlePlaceholderTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers)
        {
            tokenHandlers["{Episode Title}"] = m => null;
            tokenHandlers["{Episode CleanTitle}"] = m => null;
        }

        private void AddEpisodeTitleTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, List<Episode> episodes, int maxLength)
        {
            tokenHandlers["{Episode Title}"] = m => GetEpisodeTitle(GetEpisodeTitles(episodes), "+", maxLength, m.CustomFormat);
            tokenHandlers["{Episode CleanTitle}"] = m => GetEpisodeTitle(GetEpisodeTitles(episodes).Select(CleanTitle).ToList(), "and", maxLength, m.CustomFormat);
        }

        private void AddEpisodeFileTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, EpisodeFile episodeFile, bool useCurrentFilenameAsFallback)
        {
            tokenHandlers["{Original Title}"] = m => GetOriginalTitle(episodeFile, useCurrentFilenameAsFallback);
            tokenHandlers["{Original Filename}"] = m => GetOriginalFileName(episodeFile, useCurrentFilenameAsFallback);
            tokenHandlers["{Release Group}"] = m => episodeFile.ReleaseGroup.IsNullOrWhiteSpace() ? m.DefaultValue("Sonarr") : Truncate(episodeFile.ReleaseGroup, m.CustomFormat);
            tokenHandlers["{Release Hash}"] = m => episodeFile.ReleaseHash ?? string.Empty;
        }

        private void AddQualityTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Series series, EpisodeFile episodeFile)
        {
            var qualityTitle = _qualityDefinitionService.Get(episodeFile.Quality.Quality).Title;
            var qualityProper = GetQualityProper(series, episodeFile.Quality);
            var qualityReal = GetQualityReal(series, episodeFile.Quality);

            tokenHandlers["{Quality Full}"] = m => string.Format("{0} {1} {2}", qualityTitle, qualityProper, qualityReal);
            tokenHandlers["{Quality Title}"] = m => qualityTitle;
            tokenHandlers["{Quality Proper}"] = m => qualityProper;
            tokenHandlers["{Quality Real}"] = m => qualityReal;
        }

        private static readonly IReadOnlyDictionary<string, int> MinimumMediaInfoSchemaRevisions =
            new Dictionary<string, int>(FileNameBuilderTokenEqualityComparer.Instance)
        {
            { MediaInfoVideoDynamicRangeToken, 5 },
            { MediaInfoVideoDynamicRangeTypeToken, 11 }
        };

        private void AddMediaInfoTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, EpisodeFile episodeFile)
        {
            if (episodeFile.MediaInfo == null)
            {
                _logger.Trace("Media info is unavailable for {0}", episodeFile);

                return;
            }

            var sceneName = episodeFile.GetSceneOrFileName();

            var videoCodec = MediaInfoFormatter.FormatVideoCodec(episodeFile.MediaInfo, sceneName);
            var audioCodec = MediaInfoFormatter.FormatAudioCodec(episodeFile.MediaInfo, sceneName);
            var audioChannels = MediaInfoFormatter.FormatAudioChannels(episodeFile.MediaInfo);
            var audioLanguages = episodeFile.MediaInfo.AudioLanguages ?? new List<string>();
            var subtitles = episodeFile.MediaInfo.Subtitles ?? new List<string>();

            var videoBitDepth = episodeFile.MediaInfo.VideoBitDepth > 0 ? episodeFile.MediaInfo.VideoBitDepth.ToString() : 8.ToString();
            var audioChannelsFormatted = audioChannels > 0 ?
                                audioChannels.ToString("F1", CultureInfo.InvariantCulture) :
                                string.Empty;

            tokenHandlers["{MediaInfo Video}"] = m => videoCodec;
            tokenHandlers["{MediaInfo VideoCodec}"] = m => videoCodec;
            tokenHandlers["{MediaInfo VideoBitDepth}"] = m => videoBitDepth;

            tokenHandlers["{MediaInfo Audio}"] = m => audioCodec;
            tokenHandlers["{MediaInfo AudioCodec}"] = m => audioCodec;
            tokenHandlers["{MediaInfo AudioChannels}"] = m => audioChannelsFormatted;
            tokenHandlers["{MediaInfo AudioLanguages}"] = m => GetLanguagesToken(audioLanguages, m.CustomFormat, true, true);
            tokenHandlers["{MediaInfo AudioLanguagesAll}"] = m => GetLanguagesToken(audioLanguages, m.CustomFormat, false, true);

            tokenHandlers["{MediaInfo SubtitleLanguages}"] = m => GetLanguagesToken(subtitles, m.CustomFormat, false, true);
            tokenHandlers["{MediaInfo SubtitleLanguagesAll}"] = m => GetLanguagesToken(subtitles, m.CustomFormat, false, true);

            tokenHandlers["{MediaInfo Simple}"] = m => $"{videoCodec} {audioCodec}";

            tokenHandlers["{MediaInfo Full}"] = m => $"{videoCodec} {audioCodec}{GetLanguagesToken(audioLanguages, m.CustomFormat, true, true)} {GetLanguagesToken(subtitles, m.CustomFormat, false, true)}";

            tokenHandlers[MediaInfoVideoDynamicRangeToken] =
                m => MediaInfoFormatter.FormatVideoDynamicRange(episodeFile.MediaInfo);
            tokenHandlers[MediaInfoVideoDynamicRangeTypeToken] =
                m => MediaInfoFormatter.FormatVideoDynamicRangeType(episodeFile.MediaInfo);
        }

        private void AddCustomFormats(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Series series, EpisodeFile episodeFile, List<CustomFormat> customFormats = null)
        {
            if (customFormats == null)
            {
                episodeFile.Series = series;
                customFormats = _formatCalculator.ParseCustomFormat(episodeFile, series);
            }

            tokenHandlers["{Custom Formats}"] = m => GetCustomFormatsToken(customFormats, m.CustomFormat);
            tokenHandlers["{Custom Format}"] = m =>
            {
                if (m.CustomFormat.IsNullOrWhiteSpace())
                {
                    return string.Empty;
                }

                return customFormats.FirstOrDefault(x => x.IncludeCustomFormatWhenRenaming && x.Name == m.CustomFormat)?.ToString() ?? string.Empty;
            };
        }

        private void AddIdTokens(Dictionary<string, Func<TokenMatch, string>> tokenHandlers, Series series)
        {
            tokenHandlers["{ImdbId}"] = m => series.ImdbId ?? string.Empty;
            tokenHandlers["{TvdbId}"] = m => series.TvdbId.ToString();
            tokenHandlers["{TvMazeId}"] = m => series.TvMazeId > 0 ? series.TvMazeId.ToString() : string.Empty;
            tokenHandlers["{TmdbId}"] = m => series.TmdbId > 0 ? series.TmdbId.ToString() : string.Empty;
        }

        private string GetCustomFormatsToken(List<CustomFormat> customFormats, string filter)
        {
            var tokens = customFormats.Where(x => x.IncludeCustomFormatWhenRenaming).ToList();

            var filteredTokens = tokens;

            if (filter.IsNotNullOrWhiteSpace())
            {
                if (filter.StartsWith("-"))
                {
                    var splitFilter = filter.Substring(1).Split(',');
                    filteredTokens = tokens.Where(c => !splitFilter.Contains(c.Name)).ToList();
                }
                else
                {
                    var splitFilter = filter.Split(',');
                    filteredTokens = tokens.Where(c => splitFilter.Contains(c.Name)).ToList();
                }
            }

            return string.Join(" ", filteredTokens);
        }

        private string GetLanguagesToken(List<string> mediaInfoLanguages, string filter, bool skipEnglishOnly, bool quoted)
        {
            var tokens = new List<string>();
            foreach (var item in mediaInfoLanguages)
            {
                if (!string.IsNullOrWhiteSpace(item) && item != "und")
                {
                    tokens.Add(item.Trim());
                }
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                try
                {
                    var token = tokens[i].ToLowerInvariant();
                    if (Iso639BTMap.TryGetValue(token, out var mapped))
                    {
                        token = mapped;
                    }

                    var cultureInfo = new CultureInfo(token);
                    tokens[i] = cultureInfo.TwoLetterISOLanguageName.ToUpper();
                }
                catch
                {
                }
            }

            tokens = tokens.Distinct().ToList();

            var filteredTokens = tokens;

            // Exclude or filter
            if (filter.IsNotNullOrWhiteSpace())
            {
                if (filter.StartsWith("-"))
                {
                    filteredTokens = tokens.Except(filter.Split('-')).ToList();
                }
                else
                {
                    filteredTokens = filter.Split('+').Intersect(tokens).ToList();
                }
            }

            // Replace with wildcard (maybe too limited)
            if (filter.IsNotNullOrWhiteSpace() && filter.EndsWith("+") && filteredTokens.Count != tokens.Count)
            {
                filteredTokens.Add("--");
            }

            if (skipEnglishOnly && filteredTokens.Count == 1 && filteredTokens.First() == "EN")
            {
                return string.Empty;
            }

            var response = string.Join("+", filteredTokens);

            if (quoted && response.IsNotNullOrWhiteSpace())
            {
                return $"[{response}]";
            }
            else
            {
                return response;
            }
        }

        private void UpdateMediaInfoIfNeeded(string pattern, EpisodeFile episodeFile, Series series)
        {
            if (series.Path.IsNullOrWhiteSpace())
            {
                return;
            }

            var schemaRevision = episodeFile.MediaInfo != null ? episodeFile.MediaInfo.SchemaRevision : 0;
            var matches = TitleRegex.Matches(pattern);

            var shouldUpdateMediaInfo = matches.Cast<Match>()
                .Select(m => MinimumMediaInfoSchemaRevisions.GetValueOrDefault(m.Value, -1))
                .Any(r => schemaRevision < r);

            if (shouldUpdateMediaInfo)
            {
                _mediaInfoUpdater.Update(episodeFile, series);
            }
        }

        private string ReplaceTokens(string pattern, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, NamingConfig namingConfig, bool escape = false)
        {
            return TitleRegex.Replace(pattern, match => ReplaceToken(match, tokenHandlers, namingConfig, escape));
        }

        private string ReplaceToken(Match match, Dictionary<string, Func<TokenMatch, string>> tokenHandlers, NamingConfig namingConfig, bool escape)
        {
            if (match.Groups["escaped"].Success)
            {
                if (escape)
                {
                    return match.Value;
                }
                else if (match.Value == "{{")
                {
                    return "{";
                }
                else if (match.Value == "}}")
                {
                    return "}";
                }
            }

            var tokenMatch = new TokenMatch
            {
                RegexMatch = match,
                Prefix = match.Groups["prefix"].Value,
                Separator = match.Groups["separator"].Value,
                Suffix = match.Groups["suffix"].Value,
                Token = match.Groups["token"].Value,
                CustomFormat = match.Groups["customFormat"].Value
            };

            if (tokenMatch.CustomFormat.IsNullOrWhiteSpace())
            {
                tokenMatch.CustomFormat = null;
            }

            var tokenHandler = tokenHandlers.GetValueOrDefault(tokenMatch.Token, m => string.Empty);

            var replacementText = tokenHandler(tokenMatch);

            if (replacementText == null)
            {
                // Preserve original token if handler returned null
                return match.Value;
            }

            replacementText = replacementText.Trim();

            if (tokenMatch.Token.All(t => !char.IsLetter(t) || char.IsLower(t)))
            {
                replacementText = replacementText.ToLower();
            }
            else if (tokenMatch.Token.All(t => !char.IsLetter(t) || char.IsUpper(t)))
            {
                replacementText = replacementText.ToUpper();
            }

            if (!tokenMatch.Separator.IsNullOrWhiteSpace())
            {
                replacementText = replacementText.Replace(" ", tokenMatch.Separator);
            }

            replacementText = CleanFileName(replacementText, namingConfig);

            if (!replacementText.IsNullOrWhiteSpace())
            {
                replacementText = tokenMatch.Prefix + replacementText + tokenMatch.Suffix;
            }

            if (escape)
            {
                replacementText = replacementText.Replace("{", "{{").Replace("}", "}}");
            }

            return replacementText;
        }

        private string FormatNumberTokens(string basePattern, string formatPattern, List<Episode> episodes)
        {
            var pattern = string.Empty;

            for (var i = 0; i < episodes.Count; i++)
            {
                var patternToReplace = i == 0 ? basePattern : formatPattern;

                pattern += EpisodeRegex.Replace(patternToReplace, match => ReplaceNumberToken(match.Groups["episode"].Value, episodes[i].EpisodeNumber));
            }

            return ReplaceSeasonTokens(pattern, episodes.First().SeasonNumber);
        }

        private string FormatAbsoluteNumberTokens(string basePattern, string formatPattern, List<Episode> episodes)
        {
            var pattern = string.Empty;

            for (var i = 0; i < episodes.Count; i++)
            {
                var patternToReplace = i == 0 ? basePattern : formatPattern;

                pattern += AbsoluteEpisodeRegex.Replace(patternToReplace, match => ReplaceNumberToken(match.Groups["absolute"].Value, episodes[i].AbsoluteEpisodeNumber.Value));
            }

            return ReplaceSeasonTokens(pattern, episodes.First().SeasonNumber);
        }

        private string FormatRangeNumberTokens(string seasonEpisodePattern, string formatPattern, List<Episode> episodes)
        {
            var eps = new List<Episode> { episodes.First() };

            if (episodes.Count > 1)
            {
                eps.Add(episodes.Last());
            }

            return FormatNumberTokens(seasonEpisodePattern, formatPattern, eps);
        }

        private string ReplaceSeasonTokens(string pattern, int seasonNumber)
        {
            return SeasonRegex.Replace(pattern, match => ReplaceNumberToken(match.Groups["season"].Value, seasonNumber));
        }

        private string ReplaceNumberToken(string token, int value)
        {
            var split = token.Trim('{', '}').Split(':');
            if (split.Length == 1)
            {
                return value.ToString("0");
            }

            return value.ToString(split[1]);
        }

        private EpisodeFormat[] GetEpisodeFormat(string pattern)
        {
            return _episodeFormatCache.Get(pattern, () => SeasonEpisodePatternRegex.Matches(pattern).OfType<Match>()
                .Select(match => new EpisodeFormat
                {
                    EpisodeSeparator = match.Groups["episodeSeparator"].Value,
                    Separator = match.Groups["separator"].Value,
                    EpisodePattern = match.Groups["episode"].Value,
                    SeasonEpisodePattern = match.Groups["seasonEpisode"].Value,
                }).ToArray());
        }

        private AbsoluteEpisodeFormat[] GetAbsoluteFormat(string pattern)
        {
            return _absoluteEpisodeFormatCache.Get(pattern, () => AbsoluteEpisodePatternRegex.Matches(pattern).OfType<Match>()
                .Select(match => new AbsoluteEpisodeFormat
                {
                    Separator = match.Groups["separator"].Value.IsNotNullOrWhiteSpace() ? match.Groups["separator"].Value : "-",
                    AbsoluteEpisodePattern = match.Groups["absolute"].Value
                }).ToArray());
        }

        private bool GetPatternHasEpisodeIdentifier(string pattern)
        {
            return _patternHasEpisodeIdentifierCache.Get(pattern, () =>
            {
                if (SeasonEpisodePatternRegex.IsMatch(pattern))
                {
                    return true;
                }

                if (AbsoluteEpisodePatternRegex.IsMatch(pattern))
                {
                    return true;
                }

                if (AirDateRegex.IsMatch(pattern))
                {
                    return true;
                }

                return false;
            });
        }

        private List<string> GetEpisodeTitles(List<Episode> episodes)
        {
            if (episodes.Count == 1)
            {
                return new List<string>
                       {
                           episodes.First().Title.TrimEnd(EpisodeTitleTrimCharacters)
                       };
            }

            var titles = episodes.Select(c => c.Title.TrimEnd(EpisodeTitleTrimCharacters))
                                 .Select(CleanupEpisodeTitle)
                                 .Distinct()
                                 .ToList();

            if (titles.All(t => t.IsNullOrWhiteSpace()))
            {
                titles = episodes.Select(c => c.Title.TrimEnd(EpisodeTitleTrimCharacters))
                                 .Distinct()
                                 .ToList();
            }

            return titles;
        }

        private string GetEpisodeTitle(List<string> titles, string separator, int maxLength, string formatter)
        {
            var maxFormatterLength = GetMaxLengthFromFormatter(formatter);

            if (maxFormatterLength > 0)
            {
                maxLength = Math.Min(maxLength, maxFormatterLength);
            }

            separator = $" {separator.Trim()} ";

            var joined = string.Join(separator, titles);

            if (joined.GetByteCount() <= maxLength)
            {
                return joined;
            }

            var firstTitle = titles.First();
            var firstTitleLength = firstTitle.GetByteCount();

            if (titles.Count >= 2)
            {
                var lastTitle = titles.Last();
                var lastTitleLength = lastTitle.GetByteCount();
                if (firstTitleLength + lastTitleLength + 3 <= maxLength)
                {
                    return $"{firstTitle.TrimEnd(' ', '.')}{{ellipsis}}{lastTitle}";
                }
            }

            if (titles.Count > 1 && firstTitleLength + 3 <= maxLength)
            {
                return $"{firstTitle.TrimEnd(' ', '.')}{{ellipsis}}";
            }

            if (titles.Count == 1 && firstTitleLength <= maxLength)
            {
                return firstTitle;
            }

            return $"{firstTitle.Truncate(maxLength - 3).TrimEnd(' ', '.')}{{ellipsis}}";
        }

        private string CleanupEpisodeTitle(string title)
        {
            // this will remove (1),(2) from the end of multi part episodes.
            return MultiPartCleanupRegex.Replace(title, string.Empty).Trim();
        }

        private string GetQualityProper(Series series, QualityModel quality)
        {
            if (quality.Revision.Version > 1)
            {
                if (series.SeriesType == SeriesTypes.Anime)
                {
                    return "v" + quality.Revision.Version;
                }

                return "Proper";
            }

            return string.Empty;
        }

        private string GetQualityReal(Series series, QualityModel quality)
        {
            if (quality.Revision.Real > 0)
            {
                return "REAL";
            }

            return string.Empty;
        }

        private string GetOriginalTitle(EpisodeFile episodeFile, bool useCurrentFilenameAsFallback)
        {
            if (episodeFile.SceneName.IsNullOrWhiteSpace())
            {
                return CleanFileName(GetOriginalFileName(episodeFile, useCurrentFilenameAsFallback));
            }

            return CleanFileName(episodeFile.SceneName);
        }

        private string GetOriginalFileName(EpisodeFile episodeFile, bool useCurrentFilenameAsFallback)
        {
            if (!useCurrentFilenameAsFallback)
            {
                return string.Empty;
            }

            if (episodeFile.RelativePath.IsNullOrWhiteSpace())
            {
                return Path.GetFileNameWithoutExtension(episodeFile.Path);
            }

            return Path.GetFileNameWithoutExtension(episodeFile.RelativePath);
        }

        private int GetLengthWithoutEpisodeTitle(string pattern, NamingConfig namingConfig)
        {
            var tokenHandlers = new Dictionary<string, Func<TokenMatch, string>>(FileNameBuilderTokenEqualityComparer.Instance);
            tokenHandlers["{Episode Title}"] = m => string.Empty;
            tokenHandlers["{Episode CleanTitle}"] = m => string.Empty;
            tokenHandlers["{ellipsis}"] = m => "...";

            var result = ReplaceTokens(pattern, tokenHandlers, namingConfig);

            return result.GetByteCount();
        }

        private string ReplaceReservedDeviceNames(string input)
        {
            // Replace reserved windows device names with an alternative
            return ReservedDeviceNamesRegex.Replace(input, match => match.Value.Replace(".", "_"));
        }

        private static string CleanFileName(string name, NamingConfig namingConfig)
        {
            var result = name;

            if (namingConfig.ReplaceIllegalCharacters)
            {
                // Smart replaces a colon followed by a space with space dash space for a better appearance
                if (namingConfig.ColonReplacementFormat == ColonReplacementFormat.Smart)
                {
                    result = result.Replace(": ", " - ");
                    result = result.Replace(":", "-");
                }
                else
                {
                    var replacement = string.Empty;

                    switch (namingConfig.ColonReplacementFormat)
                    {
                        case ColonReplacementFormat.Dash:
                            replacement = "-";
                            break;
                        case ColonReplacementFormat.SpaceDash:
                            replacement = " -";
                            break;
                        case ColonReplacementFormat.SpaceDashSpace:
                            replacement = " - ";
                            break;
                        case ColonReplacementFormat.Custom:
                            replacement = namingConfig.CustomColonReplacementFormat;
                            break;
                    }

                    result = result.Replace(":", replacement);
                }
            }
            else
            {
                result = result.Replace(":", string.Empty);
            }

            for (var i = 0; i < BadCharacters.Length; i++)
            {
                result = result.Replace(BadCharacters[i], namingConfig.ReplaceIllegalCharacters ? GoodCharacters[i] : string.Empty);
            }

            return result.TrimStart(' ', '.').TrimEnd(' ');
        }

        private string Truncate(string input, string formatter)
        {
            if (input.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var maxLength = GetMaxLengthFromFormatter(formatter);

            if (maxLength == 0 || input.Length <= Math.Abs(maxLength))
            {
                return input;
            }

            if (maxLength < 0)
            {
                return $"{{ellipsis}}{input.Reverse().Truncate(Math.Abs(maxLength) - 3).TrimEnd(' ', '.').Reverse()}";
            }

            return $"{input.Truncate(maxLength - 3).TrimEnd(' ', '.')}{{ellipsis}}";
        }

        private int GetMaxLengthFromFormatter(string formatter)
        {
            int.TryParse(formatter, out var maxCustomLength);

            return maxCustomLength;
        }
    }

    internal sealed class TokenMatch
    {
        public Match RegexMatch { get; set; }
        public string Prefix { get; set; }
        public string Separator { get; set; }
        public string Suffix { get; set; }
        public string Token { get; set; }
        public string CustomFormat { get; set; }

        public string DefaultValue(string defaultValue)
        {
            if (string.IsNullOrEmpty(Prefix) && string.IsNullOrEmpty(Suffix))
            {
                return defaultValue;
            }
            else
            {
                return string.Empty;
            }
        }
    }

    public enum MultiEpisodeStyle
    {
        Extend = 0,
        Duplicate = 1,
        Repeat = 2,
        Scene = 3,
        Range = 4,
        PrefixedRange = 5
    }

    public enum ColonReplacementFormat
    {
        Delete = 0,
        Dash = 1,
        SpaceDash = 2,
        SpaceDashSpace = 3,
        Smart = 4,
        Custom = 5
    }
}
