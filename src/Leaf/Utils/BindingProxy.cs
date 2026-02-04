using System.Windows;

namespace Leaf.Utils;

/// <summary>
/// A proxy object that can be used to bind to a DataContext from outside the visual tree.
/// This is useful for ContextMenus and Popups which have their own visual tree and cannot
/// use ElementName bindings to reach elements in the main visual tree.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add as a resource: &lt;utils:BindingProxy x:Key="Proxy" Data="{Binding}" /&gt;
/// 2. Bind from ContextMenu: Command="{Binding Data.SomeCommand, Source={StaticResource Proxy}}"
/// </remarks>
public class BindingProxy : Freezable
{
    /// <summary>
    /// The data to proxy. Typically bound to a DataContext or specific object.
    /// </summary>
    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(object),
            typeof(BindingProxy),
            new PropertyMetadata(null));

    protected override Freezable CreateInstanceCore()
    {
        return new BindingProxy();
    }
}
