using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;
using Pookie.Localization;

namespace Pookie.App;

internal static class LocalizedViews
{
    internal static void Refresh(Element root)
    {
        // Logical children include hidden pages. Visual children include generated control templates.
        var seen = new HashSet<Element>();
        LogicalTree.Visit(root, Visit);
        VisualTree.Visit(root, Visit);

        void Visit(Element element)
        {
            if (!seen.Add(element)) return;
            switch (element)
            {
                case TextBlock label:
                    RefreshProperty(label, TextBlock.TextProperty, label.Text);
                    break;
                case TextBase input:
                    RefreshProperty(input, TextBase.PlaceholderProperty, input.Placeholder);
                    break;
                case InAppPopup popup:
                    popup.LabelLanguage = UiText.Language;
                    break;
                case ChatTabs tabs:
                    tabs.InvalidateLabels();
                    break;
                case MaterialSelect select:
                    select.InvalidateVisual();
                    break;
                case ModalDialog dialog:
                    dialog.InvalidateTranslationSnapshot();
                    break;
            }
            if (element is UIElement control)
            {
                RefreshProperty(control, AutomationProperties.NameProperty, AutomationProperties.GetName(control));
                RefreshProperty(control, AutomationProperties.HelpTextProperty, AutomationProperties.GetHelpText(control));
            }
        }
    }

    private static void RefreshProperty(MewObject owner, MewProperty<string> property, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var translation = UiText.Refresh(text);
        if (!ReferenceEquals(text, translation)) owner.SetCurrentValue(property, translation);
    }
}
