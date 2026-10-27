using System.Windows;
using System.Windows.Controls;

namespace VaultSync.App;

// First four destinations stay together; Settings remains anchored at the bottom.
public sealed class NavigationPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double height = 0, width = 0;
        foreach (UIElement child in InternalChildren) { child.Measure(new Size(available.Width, double.PositiveInfinity)); height += child.DesiredSize.Height; width = Math.Max(width, child.DesiredSize.Width); }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i]; double height = child.DesiredSize.Height;
            if (i == InternalChildren.Count - 1) y = Math.Max(y + 16, final.Height - height);
            child.Arrange(new Rect(0, y, final.Width, height)); y += height;
        }
        return final;
    }
}
