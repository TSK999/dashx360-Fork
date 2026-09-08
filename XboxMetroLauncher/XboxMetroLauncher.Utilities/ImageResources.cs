using System.Windows.Controls;

namespace XboxMetroLauncher.Utilities;

public static class ImageResources
{
    public static void Suspend(Image image) => image.SetCurrentValue(Image.SourceProperty, null);
    public static void Resume(Image image) => image.GetBindingExpression(Image.SourceProperty)?.UpdateTarget();
}
