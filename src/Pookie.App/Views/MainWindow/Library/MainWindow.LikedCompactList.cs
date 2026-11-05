using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ItemsControl CreateLikedCompactList()
    {
        var result = new ItemsControl().Items(Array.Empty<SoundCloudTrack>(), track => track.Title)
            .ItemHeight(64).FixedHeightPresenter().Background(Color.Transparent).BorderThickness(0).Padding(0).ItemPadding(new Thickness(0));
        result.ItemTemplate = new DelegateTemplate<SoundCloudTrack>(context =>
        {
            var row = CreateCompactTrackRow(); context.Register("row", row.Root);
            return CreateLikedItemMotion(row.Root, result, context);
        }, (_, track, _, context) =>
        {
            BindLikedItemMotion(context, track.Id);
            BindCompactTrackRow(compactTrackRows.Single(row => row.Root == context.Get<Grid>("row")), track);
        }, (_, _, _, context) =>
        {
            ClearLikedItemMotion(context);
            BindCompactTrackRow(compactTrackRows.Single(row => row.Root == context.Get<Grid>("row")), null);
        });
        return result;
    }
}
