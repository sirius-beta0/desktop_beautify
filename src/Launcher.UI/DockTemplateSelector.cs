using System.Windows;
using System.Windows.Controls;
using Launcher.Core.Indexing;

namespace Launcher.UI;

/// <summary>
/// Dock 栏项模板选择器：普通收藏用图标模板，添加收藏占位（IsDockAdd）用「+」模板。
/// </summary>
public sealed class DockTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (container is not FrameworkElement fe) return base.SelectTemplate(item, container);
        if (item is AppEntry { IsDockAdd: true })
            return (DataTemplate)fe.FindResource("DockAddTemplate");
        return (DataTemplate)fe.FindResource("DockIconTemplate");
    }
}
