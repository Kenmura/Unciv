namespace Unciv.Harness;

/// <summary>Pixel rectangle of one contact-sheet cell: the thumbnail area and the caption area below it.</summary>
public readonly record struct SheetCell(int X, int Y, int ThumbWidth, int ThumbHeight, int CaptionHeight);

public sealed record SheetLayout(int Width, int Height, IReadOnlyList<SheetCell> Cells);

/// <summary>Grid arithmetic for the owner's contact sheet, kept apart from Godot so it is tested in the normal suite.</summary>
public static class ContactSheetLayout
{
    /// <summary>Most rows a sheet may have: a viewport texture beyond the GPU limit (16384 px) renders nothing, so the capture of a big sheet came back null.</summary>
    public const int MaxRows = 40;

    /// <summary>Columns for <paramref name="count"/> cells: three, or more when three would need more than <see cref="MaxRows"/> rows.</summary>
    public static int ColumnsFor(int count) => Math.Max(3, (count + MaxRows - 1) / MaxRows);

    /// <summary>
    /// Lays <paramref name="count"/> thumbnails of the given aspect ratio row by row, <paramref name="columns"/> to a row.
    /// The thumbnail width is fixed; its height follows the source aspect (rounded up so nothing is cropped).
    /// </summary>
    public static SheetLayout Compute(int count, int sourceWidth, int sourceHeight, int columns = 3, int thumbWidth = 320, int captionHeight = 40, int margin = 8)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (sourceWidth <= 0 || sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source size must be positive");
        if (columns <= 0 || thumbWidth <= 0 || captionHeight < 0 || margin < 0) throw new ArgumentOutOfRangeException(nameof(columns));

        int thumbHeight = (int)Math.Ceiling((double)thumbWidth * sourceHeight / sourceWidth);
        int usedColumns = Math.Max(1, Math.Min(columns, count));
        int rows = count == 0 ? 0 : (count + columns - 1) / columns;
        int cellHeight = thumbHeight + captionHeight + margin;
        var cells = new List<SheetCell>(count);
        for (int i = 0; i < count; i++)
            cells.Add(new SheetCell(margin + i % columns * (thumbWidth + margin), margin + i / columns * cellHeight, thumbWidth, thumbHeight, captionHeight));
        return new SheetLayout(margin + usedColumns * (thumbWidth + margin), margin + rows * cellHeight, cells);
    }
}
