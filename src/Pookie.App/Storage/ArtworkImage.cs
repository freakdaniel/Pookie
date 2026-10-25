// SPDX-License-Identifier: BSD-3-Clause
using Aprillz.MewUI;

namespace Pookie.App.Storage;

internal static class ArtworkImage
{
    // Normalize oversized uploads on the worker. Covers use 1024 physical
    // pixels; wide profile banners can request 2048 for high-DPI displays.
    internal const int MaximumSide = 1024;
    internal const long MaximumPixels = 16L * 1024 * 1024;
    private static readonly object decoding = new();
    internal static ImageSource Prepare(byte[] payload, int maximumSide = MaximumSide)
    {
        lock (decoding)
        {
            maximumSide = Math.Clamp(maximumSide,64,2048);
            var source = ImageSource.FromBytes(payload);
            var width = source.PixelWidth; var height = source.PixelHeight;
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaximumPixels)
                throw new ArgumentException("Artwork dimensions exceed the decode budget.");
            if (Math.Max(width, height) <= maximumSide) { source.EnsureDecode(width,height); return source; }
            var input = new byte[checked(width * height * 4)];
            source.CopyPixels(input, width * 4);
            var scale = maximumSide / (double)Math.Max(width,height);
            var targetWidth = Math.Max(1,(int)Math.Round(width*scale)); var targetHeight = Math.Max(1,(int)Math.Round(height*scale));
            var pixels = new byte[targetWidth*targetHeight*4];
            // Area sampling preserves small artwork details when reducing 4K uploads.
            for(var y=0;y<targetHeight;y++)
            for(var x=0;x<targetWidth;x++)
            {
                var left=x*width/targetWidth;var right=(x+1)*width/targetWidth;
                var top=y*height/targetHeight;var bottom=(y+1)*height/targetHeight;
                var count=(right-left)*(bottom-top);
                for(var c=0;c<4;c++)
                {
                    var sum=0;
                    for(var sy=top;sy<bottom;sy++)for(var sx=left;sx<right;sx++)sum+=input[(sy*width+sx)*4+c];
                    pixels[(y*targetWidth+x)*4+c]=(byte)((sum+count/2)/count);
                }
            }
            return ImageSource.FromBgraPixels(targetWidth,targetHeight,pixels);
        }
    }
}
