using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>A column of a <see cref="DataTable"/>: the untranslated header, and whether its cells are numbers (right-aligned, tabular, sorted by value).</summary>
public readonly record struct DataColumn(string Header, bool Numeric = false);

/// <summary>
/// C065: a table in an Inset well. The header row is clickable (first click sorts ascending, the next descending, shown by a triangle), numbers are
/// right-aligned in tabular figures and rows alternate Surface1 and Surface2. The order comes from <see cref="TableSorter"/>; the rows keep their input order when equal.
/// </summary>
public partial class DataTable : PanelContainer
{
    private readonly IReadOnlyList<DataColumn> columns;
    private readonly GridContainer grid = new();
    private readonly List<Button> headers = [];
    private readonly List<List<Label>> cells = [];
    private IReadOnlyList<IReadOnlyList<string>> rows = [];
    private IReadOnlyList<int> order = [];

    public DataTable(IReadOnlyList<DataColumn> columns)
    {
        this.columns = columns;
        ThemeTypeVariation = UiVariants.PanelInset;
        grid.Columns = Math.Max(1, columns.Count);
        grid.AddThemeConstantOverride("h_separation", 0);
        grid.AddThemeConstantOverride("v_separation", 0);
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(grid);
        Rebuild();
    }

    /// <summary>The sorted column, or -1 while the rows are in input order.</summary>
    public int SortColumn { get; private set; } = -1;

    public bool SortAscending { get; private set; } = true;

    public int RowCount => rows.Count;

    /// <summary>The input row indices in display order.</summary>
    public IReadOnlyList<int> Order => order;

    /// <summary>The header button of a column (press it to sort).</summary>
    public Button HeaderButton(int column) => headers[column];

    /// <summary>The label of a cell by display position.</summary>
    public Label Cell(int displayRow, int column) => cells[displayRow][column];

    /// <summary>The text of a cell by display position.</summary>
    public string CellText(int displayRow, int column) => cells[displayRow][column].Text;

    /// <summary>Replaces the rows (each with one cell per column; missing cells are empty) and keeps the current sort.</summary>
    public void SetRows(IReadOnlyList<IReadOnlyList<string>> newRows)
    {
        rows = newRows;
        Rebuild();
    }

    /// <summary>Sorts by a column in the given direction.</summary>
    public void SortBy(int column, bool ascending)
    {
        if (column < 0 || column >= columns.Count) return;
        SortColumn = column;
        SortAscending = ascending;
        Rebuild();
    }

    private void OnHeaderPressed(int column) => SortBy(column, SortColumn != column || !SortAscending);

    private void Rebuild()
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }
        headers.Clear();
        cells.Clear();
        for (int c = 0; c < columns.Count; c++)
        {
            int column = c;
            string arrow = SortColumn != c ? "" : SortAscending ? " ▲" : " ▼";
            var header = new Button
            {
                Text = columns[c].Header.TrText() + arrow,
                ThemeTypeVariation = UiVariants.ButtonGhost,
                Alignment = columns[c].Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
            };
            header.Pressed += () => OnHeaderPressed(column);
            headers.Add(header);
            grid.AddChild(header);
        }
        order = SortColumn < 0 ? Enumerable.Range(0, rows.Count).ToList() : TableSorter.Order(rows, SortColumn, SortAscending);
        for (int r = 0; r < order.Count; r++)
        {
            var line = new List<Label>();
            var row = rows[order[r]];
            for (int c = 0; c < columns.Count; c++)
            {
                var label = new Label
                {
                    Text = c < row.Count ? row[c] : "",
                    HorizontalAlignment = columns[c].Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                if (columns[c].Numeric) label.ThemeTypeVariation = UiVariants.LabelNumber;
                var cell = new PanelContainer { ThemeTypeVariation = r % 2 == 0 ? UiVariants.PanelCellEven : UiVariants.PanelCellOdd, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                cell.AddChild(label);
                grid.AddChild(cell);
                line.Add(label);
            }
            cells.Add(line);
        }
    }
}
