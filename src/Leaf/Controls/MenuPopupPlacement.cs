using System.Windows;
using System.Windows.Controls.Primitives;

namespace Leaf.Controls;

public static class MenuPopupPlacement
{
    public static CustomPopupPlacementCallback TopLevelDropDown { get; } = PlaceTopLevelDropDown;

    private static CustomPopupPlacement[] PlaceTopLevelDropDown(Size popupSize, Size targetSize, Point offset)
    {
        return
        [
            new CustomPopupPlacement(
                new Point(0, targetSize.Height),
                PopupPrimaryAxis.Horizontal)
        ];
    }
}
