using Bloom.Core.Roblox;
using Bloom.App.Services;
using Bloom.Launcher.FastFlags;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bloom.App.Views;

public sealed class FavoriteItem(Favorite favorite)
{
    public Favorite Model { get; } = favorite;
    public string Name => Model.Name;
    public long PlaceId => Model.PlaceId;
    public BitmapImage? Icon { get; } = favorite.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;
}

public sealed class RecentItem(PlaySession session)
{
    public PlaySession Model { get; } = session;
    public string Title => Model.ExperienceName ?? (Model.PlaceId is long p ? $"Place {p}" : "Roblox");
    public bool CanLaunch => Model.PlaceId is not null;

    public string Subtitle
    {
        get
        {
            var start = Model.StartedAt.ToLocalTime();
            var duration = (Model.EndedAt ?? DateTimeOffset.Now) - Model.StartedAt;
            var length = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} h {duration.Minutes} min" : $"{Math.Max(1, (int)duration.TotalMinutes)} min";
            return $"{start:g} · {length}";
        }
    }
}

public sealed class FlagItem(FastFlag flag, AllowlistStatus status)
{
    public FastFlag Model { get; } = flag;
    public string Name => Model.Name;
    public string Value => Model.Value;
    public AllowlistStatus Status { get; } = status;

    public string StatusText => Status switch
    {
        AllowlistStatus.Allowed => Strings.Get("FlagAllowed"),
        AllowlistStatus.NotAllowed => Strings.Get("FlagNotAllowed"),
        _ => Strings.Get("FlagUnknown"),
    };
}
