using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

// The ribbon is drawn as a vector from the supplied Widevine reference.
// It is part of the card and follows the card's standard transition.
internal sealed class WidevineBrand : ContentControl
{
    internal WidevineBrand()
    {
        var ribbon = PathGeometry.Parse("M330 0H415L381 126H307Q296 126 300 138L320 210H236L215 136Q212 126 205 134L129 210L108 136Q105 126 98 126H23L0 42H84L105 116Q109 126 116 118L191 42L211 116Q214 126 222 126H287Q297 126 300 115Z");
        ribbon.Freeze();
        var letters = new StackPanel().Horizontal().Spacing(3.5).CenterVertical();
        foreach (var letter in "WIDEVINE")
            letters.Add(new TextBlock().Text(letter.ToString()).FontSize(29).FontWeight(FontWeight.Light).Foreground(Color.White));
        Content = new StackPanel().Horizontal().Spacing(17).Children(
            new PathShape().Data(ribbon).Stretch(Stretch.Uniform).Fill(Color.White).Width(72).Height(38).CenterVertical(), letters);
        Padding = new Thickness(0); BorderThickness = 0; Background = Color.Transparent;
        Height = 42; IsHitTestVisible = false;
    }

}
