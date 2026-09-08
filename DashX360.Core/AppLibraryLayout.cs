namespace XboxMetroLauncher.Utilities;

public static class AppLibraryLayout
{
    public static (double X, double Y) Position(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        // Keep the five familiar built-ins, then fill two rows in each new column.
        var (column, row) = index switch
        {
            0 => (0, 0), 1 => (1, 0), 2 => (2, 0), 3 => (0, 1), 4 => (1, 1),
            5 => (2, 1), _ => (3 + (index - 6) / 2, (index - 6) % 2)
        };
        return (16 + column * 202, 8 + row * 202);
    }
    public static double Width(int count) => Math.Max(1070, count == 0 ? 0 : Position(count - 1).X + 214);
}
