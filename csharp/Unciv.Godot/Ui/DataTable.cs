using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Ui;

/// <summary>A column of a <see cref="DataTable"/>: the untranslated header, and whether its cells are numbers (right-aligned, tabular, sorted by value).</summary>
public readonly record struct DataColumn(string Header, bool Numeric = false);

/// <summary>
/// C065: a table in an Inset well. The header row is clickable (first click sorts ascending, the next descending, shown by a triangle), numbers are
/// right-aligned in tabular figures and rows alternate Surface1 and Surface2. The order comes from <see cref="TableSorter"/>; the rows keep their input order when equal.
/// C074: with <c>actions</c> the table has one more, header-less column at the right that holds a control per row (a "go to" icon button, a row's Move and Coup buttons);
/// <see cref="SetRows"/> takes the factory, which is called once per input row, so the control stays the same object when the table is sorted.
/// </summary>
public partial class DataTable : PanelContainer
{
    private readonly IReadOnlyList<DataColumn> columns;
    private readonly GridContainer grid = new();
    private readonly List<Button> headers = [];
    private readonly List<List<Label>> cells = [];
    private IReadOnlyList<IReadOnlyList<string>> rows = [];
    private IReadOnlyList<int> order = [];
    private readonly bool hasActions;
    private IReadOnlyList<Control?> actionControls = [];

    public DataTable(IReadOnlyList<DataColumn> columns, bool actions = false)
    {
        this.columns = columns;
        hasActions = actions;
        ThemeTypeVariation = UiVariants.PanelInset;
        grid.Columns = Math.Max(1, columns.Count + (actions ? 1 : 0));
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

    /// <summary>The action control of an input row (null when the row has none or the table has no action column).</summary>
    public Control? RowAction(int inputRow) => inputRow >= 0 && inputRow < actionControls.Count ? actionControls[inputRow] : null;

    /// <summary>Replaces the rows (each with one cell per column; missing cells are empty) and keeps the current sort. <paramref name="rowAction"/> makes the control of the action column for an input row.</summary>
    public void SetRows(IReadOnlyList<IReadOnlyList<string>> newRows, Func<int, Control?>? rowAction = null)
    {
        ReleaseActions();
        foreach (var control in actionControls) control?.QueueFree();
        rows = newRows;
        actionControls = hasActions && rowAction != null ? Enumerable.Range(0, newRows.Count).Select(rowAction).ToList() : [];
        Rebuild();
    }

    /// <summary>Takes the action controls out of their cells so that freeing the cells does not free them.</summary>
    private void ReleaseActions()
    {
        foreach (var control in actionControls) control?.GetParent()?.RemoveChild(control);
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
        ReleaseActions();
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
            };
            header.Pressed += () => OnHeaderPressed(column);
            headers.Add(header);
            grid.AddChild(header);
        }
        if (hasActions) grid.AddChild(new Control());
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
            if (hasActions)
            {
                var cell = new PanelContainer { ThemeTypeVariation = r % 2 == 0 ? UiVariants.PanelCellEven : UiVariants.PanelCellOdd };
                if (RowAction(order[r]) is { } action) cell.AddChild(action);
                grid.AddChild(cell);
            }
            cells.Add(line);
        }
    }
}
