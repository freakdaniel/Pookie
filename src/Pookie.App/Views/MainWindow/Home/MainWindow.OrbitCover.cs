using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private sealed class OrbitCover : ContentControl
    {
        private readonly Image artwork;
        private readonly Border fallback;
        private readonly GlassGeometry geometry = new();
        private ImageSource? source;
        private IImage? texture;
        private ImageBrush? brush;
        private Rect brushBounds;
        internal CoverGlassBackdrop Glass { get; }
        internal OrbitCover(Image artwork, FrameworkElement loadingLayer, double buttonSize)
        {
            this.artwork = artwork;
            CornerRadius = 12;
            Padding = new Thickness(0);
            BorderThickness = 0;
            fallback = new Border().CornerRadius(CornerRadius).ClipToBounds().BorderThickness(0).Child(loadingLayer);
            Content = fallback;
            // Leave a halo for blur and refraction around the button.
            Glass = new CoverGlassBackdrop(artwork, background: this, centeredSampleSize: buttonSize + 48);
        }

        internal void RefreshArtwork()
        {
            var next = artwork.Source as ImageSource;
            if (source == next) return;
            ReleaseTexture();
            source = next;
            fallback.IsVisible = source == null;
            Glass.RefreshArtwork();
            InvalidateVisual();
        }

        protected override void OnRender(IGraphicsContext context)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            var shape = geometry.Get(Bounds, new CornerRadius(CornerRadius));
            if (source == null || source.HasAlpha) context.FillPath(shape, TrackButtons.TonalSurface);
            if (source == null) return;

            texture ??= source.CreateImage(Application.Current.GraphicsFactory);
            if (brush == null || brushBounds != Bounds)
            {
                var scale = Math.Max(Bounds.Width / texture.PixelWidth, Bounds.Height / texture.PixelHeight);
                var width = texture.PixelWidth * scale;
                var height = texture.PixelHeight * scale;
                var destination = new Rect(Bounds.Center.X - width / 2, Bounds.Center.Y - height / 2, width, height);
                brush = new ImageBrush(texture, new Rect(0, 0, texture.PixelWidth, texture.PixelHeight), destination, TileMode.None);
                brushBounds = Bounds;
            }
            // Fill the silhouette directly so its rotated edges get fractional coverage.
            var quality = context.ImageScaleQuality;
            context.ImageScaleQuality = ImageScaleQuality.HighQuality;
            try { context.FillPath(shape, brush); }
            finally { context.ImageScaleQuality = quality; }
        }

        private void ReleaseTexture()
        {
            brush = null;
            texture?.Dispose();
            texture = null;
        }

        protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnVisualRootChanged(oldRoot, newRoot);
            if (newRoot != null) return;
            Glass.Reset();
            ReleaseTexture();
        }

        protected override void OnDispose()
        {
            Glass.Dispose();
            ReleaseTexture();
            base.OnDispose();
        }
    }
}
