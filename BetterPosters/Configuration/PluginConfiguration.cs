using MediaBrowser.Model.Plugins;

namespace BetterPosters.Configuration;

/// <summary>
/// Rating sources supported by btttr.cc.
/// </summary>
public enum PosterRatingSource
{
    /// <summary>
    /// Use the btttr.cc default average rating.
    /// </summary>
    Average,

    /// <summary>
    /// IMDb rating.
    /// </summary>
    Imdb,

    /// <summary>
    /// TMDB rating.
    /// </summary>
    Tmdb,

    /// <summary>
    /// Rotten Tomatoes rating.
    /// </summary>
    RottenTomatoes,

    /// <summary>
    /// Metacritic rating.
    /// </summary>
    Metacritic,

    /// <summary>
    /// Trakt rating.
    /// </summary>
    Trakt,

    /// <summary>
    /// Letterboxd rating.
    /// </summary>
    Letterboxd,

    /// <summary>
    /// Roger Ebert rating.
    /// </summary>
    RogerEbert
}

/// <summary>
/// Poster languages supported by btttr.cc.
/// </summary>
public enum PosterLanguage
{
    /// <summary>
    /// English posters.
    /// </summary>
    English,

    /// <summary>
    /// Spanish posters.
    /// </summary>
    Spanish,

    /// <summary>
    /// French posters.
    /// </summary>
    French,

    /// <summary>
    /// German posters.
    /// </summary>
    German,

    /// <summary>
    /// Brazilian Portuguese posters.
    /// </summary>
    PortugueseBrazil,

    /// <summary>
    /// Portugal Portuguese posters.
    /// </summary>
    PortuguesePortugal,

    /// <summary>
    /// Italian posters.
    /// </summary>
    Italian,

    /// <summary>
    /// Dutch posters.
    /// </summary>
    Dutch,

    /// <summary>
    /// Polish posters.
    /// </summary>
    Polish,

    /// <summary>
    /// Russian posters.
    /// </summary>
    Russian,

    /// <summary>
    /// Turkish posters.
    /// </summary>
    Turkish,

    /// <summary>
    /// Arabic posters.
    /// </summary>
    Arabic,

    /// <summary>
    /// Japanese posters.
    /// </summary>
    Japanese,

    /// <summary>
    /// Korean posters.
    /// </summary>
    Korean,

    /// <summary>
    /// Chinese posters.
    /// </summary>
    Chinese,

    /// <summary>
    /// Hindi posters.
    /// </summary>
    Hindi,

    /// <summary>
    /// Swedish posters.
    /// </summary>
    Swedish,

    /// <summary>
    /// Czech posters.
    /// </summary>
    Czech
}

/// <summary>
/// How posters are made.
/// </summary>
public enum PosterStyle
{
    /// <summary>
    /// Built here: clean artwork (no text) with the title's logo, a soft fade and the info line (TMDb artwork).
    /// </summary>
    Studio,

    /// <summary>
    /// btttr.cc's ready-made Better Posters.
    /// </summary>
    BetterPosters
}

/// <summary>
/// What the coloured tag at the top of a studio poster says.
/// </summary>
public enum StudioTag
{
    /// <summary>No tag.</summary>
    None,

    /// <summary>The studio (movies) or network (shows), e.g. "Adult Swim".</summary>
    Studio,

    /// <summary>The first genre.</summary>
    Genre
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        EnableTrendTags = true;
        EnableQualityTags = true;
        ApplyToAllLibraries = true;
        AutoApplyNewTitles = true;
        EnableGenre = true;
        EnableRating = true;
        RatingSource = PosterRatingSource.Average;
        EnableAgeRating = false;
        Language = PosterLanguage.English;
        Style = PosterStyle.Studio;
        StudioLogo = true;
        StudioFade = 70;
        StudioShowYear = true;
        TmdbApiKey = string.Empty;
        StudioTopTag = StudioTag.None;
    }

    /// <summary>
    /// Gets or sets what the coloured tag at the top of studio posters says (coloured from the poster).
    /// </summary>
    public StudioTag StudioTopTag { get; set; }

    /// <summary>
    /// Gets or sets how posters are made (Studio: built here from TMDb artwork; BetterPosters: btttr.cc's).
    /// </summary>
    public PosterStyle Style { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether studio posters show the title's logo (off: the title in text).
    /// </summary>
    public bool StudioLogo { get; set; }

    /// <summary>
    /// Gets or sets how dark the fade at the bottom of studio posters is (0-100).
    /// </summary>
    public int StudioFade { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether studio posters show the year in the info line.
    /// </summary>
    public bool StudioShowYear { get; set; }

    /// <summary>
    /// Gets or sets an optional TMDb API key for studio posters (empty: the one Jellyfin's TMDb plugin uses).
    /// </summary>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether trend tags are enabled.
    /// </summary>
    public bool EnableTrendTags { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether quality tags are enabled.
    /// </summary>
    public bool EnableQualityTags { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the genre label is enabled.
    /// </summary>
    public bool EnableGenre { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rating label is enabled.
    /// </summary>
    public bool EnableRating { get; set; }

    /// <summary>
    /// Gets or sets the rating source.
    /// </summary>
    public PosterRatingSource RatingSource { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the age rating label is enabled.
    /// </summary>
    public bool EnableAgeRating { get; set; }

    /// <summary>
    /// Gets or sets the poster language.
    /// </summary>
    public PosterLanguage Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every movie/show library gets Better Posters
    /// (off: only libraries with "Better Posters" ticked as an image fetcher).
    /// </summary>
    public bool ApplyToAllLibraries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether new titles (and upgraded files) get their poster straight away.
    /// </summary>
    public bool AutoApplyNewTitles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the next run replaces every poster, even unchanged ones.
    /// </summary>
    public bool ForceNextRun { get; set; }
}
